using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

/// <summary>
/// Companion window split into two frames:
/// - LEFT: Inbox of whitelisted /say & /party messages that hit the trigger
/// - RIGHT: Reply/work area for the selected inbox item with buttons:
///          Post to /say, Post to /party, and Speak (window-only)
/// Also preserves an isolated "chat with AI" composer along the bottom.
/// </summary>
public sealed class ChatWindow : Window
{
    private readonly IPluginLog log;
    private readonly AiClient client;
    private readonly Configuration config;
    private readonly PersonaManager persona;
    private readonly MemoryManager memory;
    private readonly ChronicleManager chronicle;
    private readonly ChatPipe pipe;

    // ===== Isolated chat transcript (bottom composer) =====
    private string input = string.Empty;
    private readonly List<ChatMessage> history = new();
    private readonly StringBuilder responseBuffer = new();
    private CancellationTokenSource? cts;

    // ===== Channel-bridge inbox (top split) =====
    private sealed class RoutedEntry
    {
        public ChatRoute Route;
        public string Sender = string.Empty;
        public string Prompt = string.Empty;
        public string? AiReply;              // proposed reply from AutoRouteListener (editable)
        public DateTime When = DateTime.Now;
    }

    private readonly List<RoutedEntry> inbox = new();
    private int selectedIndex = -1;
    private string replyEditor = string.Empty; // editable text in the reply pane

    public ChatWindow(
        IPluginLog log,
        AiClient client,
        Configuration config,
        PersonaManager persona,
        MemoryManager memory,
        ChronicleManager chronicle,
        ChatPipe pipe)
        : base("AI Companion", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
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
            MinimumSize = new Vector2(720, 520),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    // ==== External hooks (from AutoRouteListener) ====
    public void NotifyIncoming(ChatRoute route, string sender, string message)
    {
        // Add to inbox and select
        inbox.Add(new RoutedEntry
        {
            Route = route,
            Sender = sender,
            Prompt = message,
            When = DateTime.Now
        });
        selectedIndex = inbox.Count - 1;

        // Default the reply editor to empty for fresh compose
        replyEditor = string.Empty;

        // Also surface to transcript if you like context there
        history.Add(new ChatMessage("incoming", $"[{route}] {sender}: {message}"));
        IsOpen = true;
    }

    public void NotifyProposedReply(ChatRoute route, string sender, string prompt, string reply)
    {
        // Find the latest matching entry (same sender & prompt) or just the latest for that sender
        var idx = Enumerable.Range(0, inbox.Count)
            .Reverse()
            .FirstOrDefault(i =>
                string.Equals(inbox[i].Sender, sender, StringComparison.Ordinal) &&
                string.Equals(inbox[i].Prompt, prompt, StringComparison.Ordinal));

        if (idx < 0 && inbox.Count > 0)
            idx = inbox.Count - 1;

        if (idx >= 0 && idx < inbox.Count)
        {
            inbox[idx].AiReply = reply;
            selectedIndex = idx;
            replyEditor = reply ?? string.Empty;
        }
        else
        {
            // No inbox item; create one
            inbox.Add(new RoutedEntry
            {
                Route = route,
                Sender = sender,
                Prompt = prompt,
                AiReply = reply,
                When = DateTime.Now
            });
            selectedIndex = inbox.Count - 1;
            replyEditor = reply ?? string.Empty;
        }

        // Show in local transcript as well (not sent)
        history.Add(new ChatMessage("assistant", reply));
        IsOpen = true;
    }

    public override void Draw()
    {
        try
        {
            var avail = ImGui.GetContentRegionAvail();
            var topHeight = MathF.Max(220f, avail.Y * 0.55f);
            var bottomHeight = avail.Y - topHeight - 8f;

            // === TOP AREA: Split into two frames ===
            if (ImGui.BeginChild("top", new Vector2(0, topHeight), true))
            {
                var leftWidth = MathF.Max(240f, ImGui.GetContentRegionAvail().X * 0.42f);
                ImGui.BeginChild("inbox", new Vector2(leftWidth, 0), true);
                DrawInboxPane();
                ImGui.EndChild();

                ImGui.SameLine();

                ImGui.BeginChild("reply", new Vector2(0, 0), true);
                DrawReplyPane();
                ImGui.EndChild();

                ImGui.EndChild();
            }

            // === BOTTOM AREA: Isolated chat composer + transcript ===
            if (ImGui.BeginChild("bottom", new Vector2(0, bottomHeight), true))
            {
                DrawIsolatedComposer();
                ImGui.EndChild();
            }

            // === FOOTER ===
            if (ImGui.BeginChild("footer", new Vector2(0, 0)))
            {
                var model = string.IsNullOrWhiteSpace(config.Model) ? "(unset)" : config.Model;
                var personaFlag = string.IsNullOrWhiteSpace(config.SystemPromptOverride) ? "(default persona)" : "(custom persona)";
                ImGui.TextDisabled($"Model: {model} | Streaming: {config.StreamResponses} | {personaFlag}");
                ImGui.SameLine();
                if (ImGui.Button("Settings")) Plugin.OpenSettingsWindow();
                ImGui.SameLine();
                ImGui.TextDisabled("Bridge: /say & /party → manual post.");
                ImGui.EndChild();
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "ChatWindow.Draw failed");
            ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), "ChatWindow error (see log).");
        }
    }

    // ==================== UI PANES ====================

    private void DrawInboxPane()
    {
        ImGui.Text("Incoming (triggered + whitelisted)");
        ImGui.Separator();

        // Toolbar
        if (ImGui.Button("Clear All"))
        {
            inbox.Clear();
            selectedIndex = -1;
            replyEditor = string.Empty;
        }
        ImGui.SameLine();
        if (ImGui.Button("Remove Selected") && selectedIndex >= 0 && selectedIndex < inbox.Count)
        {
            inbox.RemoveAt(selectedIndex);
            selectedIndex = Math.Min(selectedIndex, inbox.Count - 1);
            replyEditor = selectedIndex >= 0 ? (inbox[selectedIndex].AiReply ?? string.Empty) : string.Empty;
        }

        ImGui.Separator();

        // List
        ImGui.BeginChild("inbox-list", new Vector2(0, 0), false);
        for (int i = 0; i < inbox.Count; i++)
        {
            var it = inbox[i];
            var tag = it.Route == ChatRoute.Party ? "[Party]" : "[Say]";
            var line = $"{tag} {it.Sender}: {Trunc(it.Prompt, 60)}";
            var selected = (i == selectedIndex);
            if (ImGui.Selectable(line, selected))
            {
                selectedIndex = i;
                replyEditor = it.AiReply ?? string.Empty;
            }
        }
        ImGui.EndChild();
    }

    private void DrawReplyPane()
    {
        ImGui.Text("Reply");
        ImGui.SameLine();
        ImGui.TextDisabled("(AI Nunu’s response workspace)");

        ImGui.Separator();

        if (selectedIndex < 0 || selectedIndex >= inbox.Count)
        {
            ImGui.TextDisabled("No incoming message selected.");
            return;
        }

        var it = inbox[selectedIndex];
        var aiName = string.IsNullOrWhiteSpace(config.AiDisplayName) ? "AI Nunu" : config.AiDisplayName;

        // Context display
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 1f, 1f), $"{(it.Route == ChatRoute.Party ? "Party" : "Say")} • {it.Sender}");
        ImGui.PushTextWrapPos();
        ImGui.TextDisabled(it.Prompt);
        ImGui.PopTextWrapPos();

        ImGui.Separator();

        // Reply editor
        if (replyEditor.Length == 0 && !string.IsNullOrEmpty(it.AiReply))
            replyEditor = it.AiReply!;

        ImGui.InputTextMultiline("##replyEditor", ref replyEditor, 8000, new Vector2(-120, 140));

        ImGui.SameLine();
        if (ImGui.BeginChild("reply-controls", new Vector2(110, 140), true))
        {
            // Generate / Regenerate via AI (non-streaming)
            if (ImGui.Button(it.AiReply == null ? "Generate" : "Regenerate", new Vector2(100, 32)))
            {
                _ = GenerateReplyFor(it);
            }

            // Accept (use edit box → set AiReply)
            if (ImGui.Button("Use Edit", new Vector2(100, 28)))
            {
                it.AiReply = replyEditor.Trim();
                history.Add(new ChatMessage("assistant", it.AiReply ?? string.Empty));
            }

            // Speak in window (append to transcript only)
            if (ImGui.Button("Speak", new Vector2(100, 28)))
            {
                var text = (replyEditor ?? string.Empty).Trim();
                if (text.Length > 0)
                    history.Add(new ChatMessage("assistant", text));
            }

            ImGui.EndChild();
        }

        ImGui.Separator();

        // Post routing buttons
        var useText = (replyEditor ?? string.Empty).Trim();
        if (useText.Length == 0 && !string.IsNullOrEmpty(it.AiReply))
            useText = it.AiReply!.Trim();

        var payload = $"{aiName} -> {it.Sender}: {useText}";
        var canPost = useText.Length > 0;

        // Buttons
        if (!canPost) ImGui.BeginDisabled();
        if (ImGui.Button("Post to /say", new Vector2(140, 30)))
            _ = pipe.SendToAsync(ChatRoute.Say, payload);
        ImGui.SameLine();
        if (ImGui.Button("Post to /party", new Vector2(150, 30)))
            _ = pipe.SendToAsync(ChatRoute.Party, payload);
        if (!canPost) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Copy", new Vector2(90, 30)))
        {
            ImGui.SetClipboardText(payload);
        }
    }

    private void DrawIsolatedComposer()
    {
        // Transcript (left)
        var leftWidth = MathF.Max(340f, ImGui.GetContentRegionAvail().X * 0.55f);
        ImGui.BeginChild("isolated-transcript", new Vector2(leftWidth, 0), true);
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

        ImGui.SameLine();

        // Composer (right)
        ImGui.BeginChild("isolated-composer", new Vector2(0, 0), true);

        ImGui.InputTextMultiline("##input", ref input, 8000, new Vector2(-6, 120));

        var sending = cts != null;
        var canSend = !sending && !string.IsNullOrWhiteSpace(input);

        if (sending) ImGui.BeginDisabled();
        if (ImGui.Button("Send (isolated)", new Vector2(160, 30)) && canSend)
            _ = SendAsync();
        if (sending) ImGui.EndDisabled();

        ImGui.SameLine();
        if (!sending && ImGui.Button("Clear", new Vector2(100, 30)))
        {
            history.Clear();
            responseBuffer.Clear();
        }

        ImGui.EndChild();
    }

    // ==================== Actions ====================

    private async Task GenerateReplyFor(RoutedEntry it)
    {
        try
        {
            var historyLite = new List<ChatMessage>(); // stateless by default
            var reply = await client.ChatOnceAsync(historyLite, it.Prompt, CancellationToken.None).ConfigureAwait(false);
            it.AiReply = reply;
            replyEditor = reply ?? string.Empty;
            history.Add(new ChatMessage("assistant", replyEditor));
        }
        catch (Exception ex)
        {
            log.Error(ex, "GenerateReplyFor failed");
            history.Add(new ChatMessage("assistant", $"(error) {ex.Message}"));
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
        catch (OperationCanceledException)
        {
            log.Info("AI request canceled by user.");
        }
        catch (Exception ex)
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

    // ==================== Utils ====================

    private static string Trunc(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Length <= max) return s;
        return s.Substring(0, Math.Max(0, max - 1)) + "…";
    }
}
