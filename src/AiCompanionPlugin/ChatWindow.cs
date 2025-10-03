using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;

namespace AiCompanionPlugin;

public sealed class ChatWindow : Window
{
    private readonly IPluginLog log;
    private readonly AiClient client;
    private readonly Configuration config;
    private readonly PersonaManager persona;
    private readonly ChatPipe pipe;

    private string input = string.Empty;
    private readonly List<ChatMessage> history = new();
    private readonly StringBuilder responseBuffer = new();
    private CancellationTokenSource? cts;

    public ChatWindow(IPluginLog log, AiClient client, Configuration config, PersonaManager persona, ChatPipe pipe)
        : base("AI Companion — Nunu")
    {
        this.log = log;
        this.client = client;
        this.config = config;
        this.persona = persona;
        this.pipe = pipe;
        this.IsOpen = false;
    }

    public void OnRoutedMessage(XivChatType fromChannel, string fromName, string content)
    {
        var prefix = fromChannel == XivChatType.Party ? "[P]" : "[S]";
        history.Add(new ChatMessage("user", $"{prefix} {fromName}: {content}"));
        // You could auto-start a reply here if you want.
    }

    public override void Draw()
    {
        ImGui.BeginChild("chat-scroll", new Vector2(0, -95), true);
        foreach (var m in history)
        {
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted($"{m.Role.ToUpperInvariant()}: {m.Content}");
            ImGui.PopTextWrapPos();
            ImGui.Separator();
        }
        if (responseBuffer.Length > 0)
        {
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted($"{config.AiDisplayName}: {responseBuffer}");
            ImGui.PopTextWrapPos();
            ImGui.Separator();
        }
        ImGui.EndChild();

        ImGui.InputTextMultiline("##input", ref input, 8000, new Vector2(-120, 80));
        ImGui.SameLine();
        if (ImGui.BeginChild("controls", new Vector2(110, 80)))
        {
            var sending = cts != null;
            var sendDisabled = sending || string.IsNullOrWhiteSpace(input);
            if (ImGui.Button(sending ? "Sending" : "Send", new Vector2(100, 36)) && !sendDisabled)
            {
                _ = SendAsync();
            }

            if (ImGui.Button("To /say", new Vector2(100, 36)))
            {
                var text = responseBuffer.Length > 0 ? responseBuffer.ToString() : (history.Count > 0 ? history[^1].Content : string.Empty);
                if (!string.IsNullOrWhiteSpace(text)) pipe.EnqueueSay(text);
            }

            if (ImGui.Button("To /party", new Vector2(100, 36)))
            {
                var text = responseBuffer.Length > 0 ? responseBuffer.ToString() : (history.Count > 0 ? history[^1].Content : string.Empty);
                if (!string.IsNullOrWhiteSpace(text)) pipe.EnqueueParty(text);
            }

            ImGui.EndChild();
        }

        if (ImGui.BeginChild("footer", new Vector2(0, 0)))
        {
            ImGui.TextDisabled($"Model: {config.Model} | Streaming: {config.StreamResponses}");
            if (ImGui.Button("Settings")) Plugin.OpenSettingsWindow();   // ← fixed (no event invoke)
            ImGui.SameLine();
            ImGui.TextDisabled("Window is isolated unless Party/Say routing is used.");
            ImGui.EndChild();
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
                    responseBuffer.Append(token);

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
            log.Information("AI request canceled by user.");
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
