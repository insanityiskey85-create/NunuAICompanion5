using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

/// <summary>
/// Listens to Party and Say. On whitelisted trigger, streams reply back to SAME channel.
/// Implements the API 13 signature with timestamp.
/// </summary>
public sealed class AutoRouteListener : IDisposable
{
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Configuration cfg;
    private readonly AiClient client;
    private readonly ChatPipe pipe;

    private int debugCount = 0;

    public AutoRouteListener(IChatGui chat, IPluginLog log, Configuration cfg, AiClient client, ChatPipe pipe)
    {
        this.chat = chat;
        this.log = log;
        this.cfg = cfg;
        this.client = client;
        this.pipe = pipe;

        // Subscribe with API 13 signature (timestamped)
        chat.ChatMessage += OnChatMessage;
        log.Info("AutoRouteListener: subscribed to ChatGui.ChatMessage (timestamp signature).");
    }

    // API 13 delegate: (XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        try
        {
            // --- DEBUG TAP ---
            if (cfg.DebugChatTap && debugCount < Math.Max(10, cfg.DebugChatTapLimit))
            {
                debugCount++;
                log.Info($"[ChatTap #{debugCount}] {type} @{timestamp} Sender='{sender.TextValue}' Msg='{message.TextValue}'");
            }

            var route = type switch
            {
                XivChatType.Party => ChatRoute.Party,
                XivChatType.Say => ChatRoute.Say,
                // Some environments may surface cross-party differently; keep a safety alias if needed.
                _ => (ChatRoute?)null
            };
            if (route == null) return;

            if (!IsListenerEnabled(route.Value)) return;

            if (!IsPipeEnabled(route.Value))
            {
                chat.PrintError($"[AI Companion] {RouteName(route.Value)} listener is ON, but its pipe is OFF. Enable it in Settings.");
                return;
            }

            var senderName = NormalizeName(sender.TextValue);
            var msg = (message.TextValue ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(msg)) return;

            var trigger = GetTrigger(route.Value);
            if (string.IsNullOrWhiteSpace(trigger) || !msg.StartsWith(trigger, StringComparison.OrdinalIgnoreCase))
                return;

            if (!IsWhitelisted(route.Value, senderName))
            {
                log.Info($"AutoRoute: blocked non-whitelisted sender '{sender.TextValue}' on {route}.");
                return;
            }

            var prompt = msg.Substring(trigger.Length).TrimStart(':', ' ', '-', '—').Trim();
            if (string.IsNullOrWhiteSpace(prompt) || !GetAutoReply(route.Value)) return;

            _ = RespondStreamAsync(route.Value, prompt, senderName);
        }
        catch (Exception ex)
        {
            log.Error(ex, "AutoRouteListener OnChatMessage failed");
            chat.PrintError("[AI Companion] Chat listener error (see log).");
        }
    }

    private async Task RespondStreamAsync(ChatRoute route, string prompt, string caller)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

            if (GetEchoCaller(route))
            {
                var aiName = string.IsNullOrWhiteSpace(cfg.AiDisplayName) ? "AI Nunu" : cfg.AiDisplayName;
                var echoFmt = GetCallerEchoFormat(route);
                var echo = (echoFmt ?? "{caller} -> {ai}: {prompt}")
                    .Replace("{caller}", caller)
                    .Replace("{ai}", aiName)
                    .Replace("{prompt}", prompt);
                await pipe.SendToAsync(route, echo, cts.Token, addPrefix: false).ConfigureAwait(false);
            }

            // Replace the header build lines inside RespondStreamAsync(...)
            var ai = string.IsNullOrWhiteSpace(cfg.AiDisplayName) ? "AI Nunu" : cfg.AiDisplayName;
            var replyFmt = GetAiReplyFormat(route);
            var arrow = cfg.UseAsciiHeaders ? "->" : "→";

            // Force ASCII arrow if chosen; minimal string replace
            var headerTemplate = (replyFmt ?? "{ai} -> {caller}: {reply}")
                .Replace("→", arrow)
                .Replace("{ai}", ai)
                .Replace("{caller}", caller)
                .Replace("{reply}", string.Empty);
            var header = headerTemplate;


            var tokens = client.ChatStreamAsync(new List<ChatMessage>(), $"Caller: {caller}\n\n{prompt}", cts.Token);
            var ok = await pipe.SendStreamingToAsync(route, tokens, header, cts.Token).ConfigureAwait(false);

            if (!ok)
                chat.PrintError($"[AI Companion] {RouteName(route)} pipe disabled — cannot reply to trigger here.");
        }
        catch (OperationCanceledException) { /* quiet */ }
        catch (Exception ex)
        {
            log.Error(ex, "AutoRouteListener RespondStreamAsync error");
            chat.PrintError("[AI Companion] Failed to stream reply (see log).");
        }
    }

    // ----- per-route config helpers -----
    private bool IsListenerEnabled(ChatRoute route) =>
        route == ChatRoute.Party ? cfg.EnablePartyListener :
        route == ChatRoute.Say ? cfg.EnableSayListener : false;

    private bool IsPipeEnabled(ChatRoute route) =>
        route == ChatRoute.Party ? cfg.EnablePartyPipe :
        route == ChatRoute.Say ? cfg.EnableSayPipe : false;

    private string GetTrigger(ChatRoute route) =>
        route == ChatRoute.Party ? (cfg.PartyTrigger ?? "!AI Nunu") :
        route == ChatRoute.Say ? (cfg.SayTrigger ?? "!AI Nunu") : "!AI Nunu";

    private bool IsWhitelisted(ChatRoute route, string caller)
    {
        var list = route == ChatRoute.Party ? cfg.PartyWhitelist : cfg.SayWhitelist;

        // If RequireWhitelist=false and list empty -> allow everyone (testing)
        if (!cfg.RequireWhitelist && (list == null || list.Count == 0))
            return true;

        return (list?.Any() ?? false) && list.Any(w => string.Equals(NormalizeName(w), caller, StringComparison.OrdinalIgnoreCase));
    }

    private bool GetAutoReply(ChatRoute route) =>
        route == ChatRoute.Party ? cfg.PartyAutoReply :
        route == ChatRoute.Say ? cfg.SayAutoReply : false;

    private bool GetEchoCaller(ChatRoute route) =>
        route == ChatRoute.Party ? cfg.PartyEchoCallerPrompt :
        route == ChatRoute.Say ? cfg.SayEchoCallerPrompt : false;

    private string GetCallerEchoFormat(ChatRoute route) =>
        route == ChatRoute.Party ? cfg.PartyCallerEchoFormat :
        route == ChatRoute.Say ? cfg.SayCallerEchoFormat : "{caller} -> {ai}: {prompt}";

    private string GetAiReplyFormat(ChatRoute route) =>
        route == ChatRoute.Party ? cfg.PartyAiReplyFormat :
        route == ChatRoute.Say ? cfg.SayAiReplyFormat : "{ai} -> {caller}: {reply}";

    private static string RouteName(ChatRoute route) => route == ChatRoute.Party ? "Party" : "Say";

    // ----- utils -----
    private static string NormalizeName(string name)
    {
        var n = name ?? string.Empty;
        var at = n.IndexOf('@'); if (at >= 0) n = n[..at];
        return string.Join(' ', n.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
    }

    public void Dispose()
    {
        chat.ChatMessage -= OnChatMessage;
        log.Info("AutoRouteListener: unsubscribed from ChatGui.ChatMessage.");
    }
}
