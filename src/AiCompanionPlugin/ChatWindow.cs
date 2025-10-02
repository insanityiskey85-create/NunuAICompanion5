using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

    private string input = string.Empty;
    private readonly List<ChatMessage> history = new();
    private readonly StringBuilder responseBuffer = new();
    private CancellationTokenSource? cts;

    public ChatWindow(IPluginLog log, AiClient client, Configuration config, PersonaManager persona, MemoryManager memory, ChronicleManager chronicle)
        : base("AI Companion", ImGuiWindowFlags.None)
    {
        this.log = log; this.client = client; this.config = config; this.persona = persona; this.memory = memory; this.chronicle = chronicle;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 380),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public override void Draw()
    {
        var pops = ThemePalette.ApplyTheme(config.ThemeName ?? "Eorzean Night");

        ImGui.BeginChild("chat-scroll", new Vector2(0, -95), true);
        foreach (var m in history)
        {
            var label = m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                ? (string.IsNullOrWhiteSpace(config.AiDisplayName) ? "AI Nunu" : config.AiDisplayName)
                : m.Role.ToUpperInvariant();
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted($"{label}: {m.Content}");
            ImGui.PopTextWrapPos();
            ImGui.Separator();
        }
        if (responseBuffer.Length > 0)
        {
            var aiName = string.IsNullOrWhiteSpace(config.AiDisplayName) ? "AI Nunu" : config.AiDisplayName;
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted($"{aiName}: {responseBuffer}");
            ImGui.PopTextWrapPos();
            ImGui.Separator();
        }
        ImGui.EndChild();

        ImGui.InputTextMultiline("##input", ref input, 8000, new Vector2(-180, 80));
        ImGui.SameLine();
        if (ImGui.BeginChild("controls", new Vector2(170, 80)))
        {
            var sending = cts != null;
            var sendDisabled = sending || string.IsNullOrWhiteSpace(input);
            if (ImGui.Button(sending ? "Sending" : "Send", new Vector2(160, 36)) && !sendDisabled)
            {
                _ = SendAsync();
            }

            if (sending)
            {
                if (ImGui.Button("Cancel", new Vector2(160, 36)))
                    cts?.Cancel();
            }
            else
            {
                if (ImGui.Button("Clear", new Vector2(160, 36)))
                {
                    history.Clear();
                    responseBuffer.Clear();
                }
            }
            ImGui.EndChild();
        }

        if (ImGui.BeginChild("footer", new Vector2(0, 0)))
        {
            ImGui.TextDisabled($"Model: {config.Model} | Streaming: {config.StreamResponses} | Theme: {config.ThemeName}");
            if (ImGui.Button("Settings")) Plugin.OpenSettingsWindow();
            ImGui.SameLine();
            if (ImGui.Button("Chronicle")) Plugin.OpenChronicleWindow();
            ImGui.SameLine();
            if (ImGui.Button("Save Last As Memory"))
            {
                if (history.Count > 0 && config.EnableMemory)
                {
                    var last = history[^1];
                    memory.Add(last.Role, last.Content);
                    if (!config.AutoSaveMemory) memory.Save();
                }
            }
            ImGui.SameLine();
            ImGui.TextDisabled("Private window only.");
            ImGui.EndChild();
        }

        ThemePalette.PopTheme(pops);
    }

    private async Task SendAsync()
    {
        var text = input.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        var userMsg = new ChatMessage("user", text);
        history.Add(userMsg);
        if (config.EnableMemory && config.AutoSaveMemory) memory.Add(userMsg.Role, userMsg.Content);

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
                var aiMsg = new ChatMessage("assistant", full);
                history.Add(aiMsg);
                responseBuffer.Clear();

                if (config.EnableMemory && config.AutoSaveMemory)
                    memory.Add(aiMsg.Role, aiMsg.Content);

                if (config.EnableChronicle && config.ChronicleAutoAppend)
                    chronicle.AppendExchange(userMsg.Content, aiMsg.Content, config.Model);
            }
            else
            {
                var full = await client.ChatOnceAsync(history, string.Empty, cts.Token);
                var aiMsg = new ChatMessage("assistant", full);
                history.Add(aiMsg);

                if (config.EnableMemory && config.AutoSaveMemory)
                    memory.Add(aiMsg.Role, aiMsg.Content);

                if (config.EnableChronicle && config.ChronicleAutoAppend)
                    chronicle.AppendExchange(userMsg.Content, aiMsg.Content, config.Model);
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
