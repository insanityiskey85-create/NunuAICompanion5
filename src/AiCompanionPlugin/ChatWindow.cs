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
        this.log = log; this.client = client; this.config = config; this.persona = persona;
        this.memory = memory; this.chronicle = chronicle; this.pipe = pipe;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 380),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public override void Draw()
    {
        try
        {
            // Transcript
            ImGui.BeginChild("chat-scroll", new Vector2(0, -95), true);
            foreach (var m in history)
            {
                ImGui.PushTextWrapPos();
                ImGui.TextUnformatted($"{(m.Role == "assistant" ? (config.AiDisplayName ?? "AI Nunu") : "You")}: {m.Content}");
                ImGui.PopTextWrapPos();
                ImGui.Separator();
            }
            if (responseBuffer.Length > 0)
            {
                ImGui.PushTextWrapPos();
                ImGui.TextUnformatted($"{config.AiDisplayName ?? "AI Nunu"} (typing): {responseBuffer}");
                ImGui.PopTextWrapPos();
                ImGui.Separator();
            }
            ImGui.EndChild();

            // Input & controls
            ImGui.InputTextMultiline("##input", ref input, 8000, new Vector2(-120, 80));
            ImGui.SameLine();
            if (ImGui.BeginChild("controls", new Vector2(110, 80)))
            {
                var sending = cts != null;
                var sendDisabled = sending || string.IsNullOrWhiteSpace(input);
                if (ImGui.Button(sending ? "Sending" : "Send", new Vector2(100, 36)) && !sendDisabled)
                    _ = SendAsync();

                if (sending)
                {
                    if (ImGui.Button("Cancel", new Vector2(100, 36))) cts?.Cancel();
                }
                else
                {
                    if (ImGui.Button("Clear", new Vector2(100, 36)))
                    {
                        history.Clear();
                        responseBuffer.Clear();
                    }
                }
                ImGui.EndChild();
            }

            // Footer
            if (ImGui.BeginChild("footer", new Vector2(0, 0)))
            {
                ImGui.TextDisabled($"Model: {config.Model} | Streaming: {config.StreamResponses} | Persona: {(string.IsNullOrWhiteSpace(config.SystemPromptOverride) ? "(default)" : "(custom)")}");
                ImGui.SameLine();
                if (ImGui.Button("Settings"))
                    Plugin.OpenSettingsWindow();
                ImGui.SameLine();
                ImGui.TextDisabled("Isolated window.");
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
            }
            else
            {
                var full = await client.ChatOnceAsync(history, string.Empty, cts.Token);
                history.Add(new ChatMessage("assistant", full));
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
