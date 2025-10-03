using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui; // use Dalamud’s ImGui

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
        : base("AI Companion", ImGuiWindowFlags.None)
    {
        this.log = log; this.client = client; this.config = config; this.persona = persona; this.pipe = pipe;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(540, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public override void Draw()
    {
        // transcript
        ImGui.BeginChild("chat-scroll", new Vector2(0, -105), true);
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

        // input & controls
        ImGui.InputTextMultiline("##input", ref input, 8000, new Vector2(-140, 90));
        ImGui.SameLine();
        if (ImGui.BeginChild("controls", new Vector2(130, 90)))
        {
            var sending = cts != null;
            var sendDisabled = sending || string.IsNullOrWhiteSpace(input);

            if (ImGui.Button(sending ? "Sending…" : "Send", new Vector2(120, 34)) && !sendDisabled)
                _ = SendAsync();

            if (sending)
            {
                if (ImGui.Button("Cancel", new Vector2(120, 34)))
                    cts?.Cancel();
            }
            else
            {
                if (ImGui.Button("Settings", new Vector2(120, 34)))
                    Plugin.PluginInterface.UiBuilder.OpenConfigUi();
            }
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
        catch (System.OperationCanceledException) { log.Info("AI request canceled."); }
        catch (System.Exception ex)
        {
            responseBuffer.Clear();
            history.Add(new ChatMessage("assistant", $"Error: {ex.Message}"));
            log.Error(ex, "AI request failed");
        }
        finally
        {
            cts?.Dispose();
            cts = null;
        }
    }
}
