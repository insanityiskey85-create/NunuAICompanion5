using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

public sealed class ChatWindow : Window
{
    private readonly IPluginLog log;
    private readonly AiClient client;
    private readonly Configuration config;
    private readonly PersonaManager persona;
    private readonly MemoryManager memory;
    private readonly ChronicleManager chronicle;
    private readonly ChatPipe pipe;

    private string input = string.Empty;
    private readonly List<ChatMessage> history = new();
    private readonly StringBuilder responseBuffer = new();
    private CancellationTokenSource? cts;

    // External (routed) context
    private string lastIncomingSender = string.Empty;
    private string lastIncomingPrompt = string.Empty;
    private ChatRoute? lastIncomingRoute = null;
    private string? pendingReplyForLastIncoming = null;

    public ChatWindow(
        IPluginLog log,
        AiClient client,
        Configuration config,
        PersonaManager persona,
        MemoryManager memory,
        ChronicleManager chronicle,
        ChatPipe pipe)
        : base("AI Companion", ImGuiWindowFlags.None)
    {
        this.log = log;
        this.client = client;
        this.config = config;
        this.persona = persona;
        this.memory = memory;
        this.chronicle = chronicle;
        this.pipe = pipe;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(540, 460),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    // ==== External hooks (from AutoRouteListener) ====
    public void NotifyIncoming(ChatRoute route, string sender, string message)
    {
        lastIncomingRoute = route;
        lastIncomingSender = sender;
        lastIncomingPrompt = message;
        pendingReplyForLastIncoming = null;

        history.Add(new ChatMessage("incoming", $"[{route}] {sender}: {message}"));
        IsOpen = true;
    }

    public void NotifyProposedReply(ChatRoute route, string sender, string prompt, string reply)
    {
        lastIncomingRoute = route;
        lastIncomingSender = sender;
        lastIncomingPrompt = prompt;
        pendingReplyForLastIncoming = reply;

        // Show proposed reply in the local transcript (not sent to game yet)
        history.Add(new ChatMessage("assistant", reply));
        IsOpen = true;
    }

    public override void Draw()
    {
        try
        {
            // Transcript
            ImGui.BeginChild("chat-scroll", new Vector2(0, -130), true);
            for (int i = 0; i < history.Count; i++)
            {
                var m = history[i];
                ImGui.PushTextWrapPos();

                string who = m.Role switch
                {
                    "assistant" => string.IsNullOrWhiteSpace(config.AiDisplayName) ? "AI Nunu" : config.AiDisplayName,
                    "incoming" => "From Channel",
                    _ => "You"
                };

                ImGui.TextUnformatted($"{who}: {m.Content}");
                ImGui.PopTextWrapPos();
                if (i < history.Count - 1) ImGui.Separator();
            }

            if (responseBuffer.Length > 0)
            {
                ImGui.Separator();
                ImGui.PushTextWrapPos();
                var typingName = string.IsNullOrWhiteSpace(config.AiDisplayName) ? "AI Nunu" : config.AiDisplayName;
                ImGui.TextUnformatted($"{typingName} (typing): {responseBuffer}");
                ImGui.PopTextWrapPos();
            }
            ImGui.EndChild();

            // Input & controls
            ImGui.InputTextMultiline("##input", ref input, 8000, new Vector2(-230, 100));
            ImGui.SameLine();
            if (ImGui.BeginChild("controls", new Vector2(220, 100)))
            {
                var sending = cts != null;
                var canSend = !sending && !string.IsNullOrWhiteSpace(input);

                if (ImGui.Button(sending ? "Sending" : "Send (isolated)", new Vector2(210, 32)) && canSend)
                {
                    _ = SendAsync();
                }

                if (sending)
                {
                    if (ImGui.Button("Cancel", new Vector2(210, 30)))
                        cts?.Cancel();
                }
                else
                {
                    if (ImGui.Button("Clear Transcript", new Vector2(210, 30)))
                    {
                        history.Clear();
                        responseBuffer.Clear();
                        pendingReplyForLastIncoming = null;
                    }
                }

                // Routed reply posting buttons (if we have a proposed reply from listener)
                if (!string.IsNullOrEmpty(pendingReplyForLastIncoming) && !string.IsNullOrWhiteSpace(lastIncomingSender))
                {
                    ImGui.Separator();
                    var reply = pendingReplyForLastIncoming!;
                    var aiName = string.IsNullOrWhiteSpace(config.AiDisplayName) ? "AI Nunu" : config.AiDisplayName;

                    if (ImGui.Button($"Post to /say → {lastIncomingSender}", new Vector2(210, 28)))
                    {
                        _ = pipe.SendToAsync(ChatRoute.Say, $"{aiName} -> {lastIncomingSender}: {reply}");
                    }
                    if (ImGui.Button($"Post to /party → {lastIncomingSender}", new Vector2(210, 28)))
                    {
                        _ = pipe.SendToAsync(ChatRoute.Party, $"{aiName} -> {lastIncomingSender}: {reply}");
                    }
                }

                ImGui.EndChild();
            }

            // Footer
            if (ImGui.BeginChild("footer", new Vector2(0, 0)))
            {
                var model = string.IsNullOrWhiteSpace(config.Model) ? "(unset)" : config.Model;
                var personaFlag = string.IsNullOrWhiteSpace(config.SystemPromptOverride) ? "(default persona)" : "(custom persona)";
                ImGui.TextDisabled($"Model: {model} | Streaming: {config.StreamResponses} | {personaFlag}");
                ImGui.SameLine();
                if (ImGui.Button("Settings")) Plugin.OpenSettingsWindow();
                ImGui.SameLine();
                ImGui.TextDisabled("Isolated chat + channel bridge (reply buttons).");
                ImGui.EndChild();
            }
        }
        catch (System.Exception ex)
        {
            log.Error(ex, "ChatWindow.Draw failed");
            ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), "ChatWindow error (see log).");
        }
    }

    private async Task SendAsync()
    {
        var text = input.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        // push user message
        history.Add(new ChatMessage("user", text));
        input = string.Empty;
        responseBuffer.Clear();

        cts = new CancellationTokenSource();
        try
        {
            if (config.StreamResponses)
            {
                await foreach (var token in client.ChatStreamAsync(history, string.Empty, cts.Token))
                {
                    responseBuffer.Append(token);
                }

                var full = responseBuffer.ToString();
                history.Add(new ChatMessage("assistant", full));
                responseBuffer.Clear();

                if (config.EnableMemory)
                {
                    var aiNm = string.IsNullOrWhiteSpace(config.AiDisplayName) ? "AI Nunu" : config.AiDisplayName;
                    var userText = history.Count >= 2 ? history[^2].Content : text;
                    memory.AppendTurn("You", userText, aiNm, full);
                }
            }
            else
            {
                var full = await client.ChatOnceAsync(history, string.Empty, cts.Token);
                history.Add(new ChatMessage("assistant", full));

                if (config.EnableMemory)
                {
                    var aiNm = string.IsNullOrWhiteSpace(config.AiDisplayName) ? "AI Nunu" : config.AiDisplayName;
                    memory.AppendTurn("You", text, aiNm, full);
                }
            }
        }
        catch (System.OperationCanceledException)
        {
            log.Info("AI request canceled by user.");
        }
        catch (System.Exception ex)
        {
            responseBuffer.Clear();
            var msg = $"Error: {ex.Message}";
            history.Add(new ChatMessage("assistant", msg));
            log.Error(ex, "AI request failed");
        }
        finally
        {
            cts?.Dispose();
            cts = null;
        }
    }
}
