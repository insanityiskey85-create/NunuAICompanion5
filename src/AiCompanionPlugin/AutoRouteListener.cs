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
/// Listens to multiple channels (currently Party and Say).
/// When a whitelisted caller posts a message starting with that channel's trigger,
/// Nunu streams her reply back to the SAME channel.
/// </summary>
public sealed class AutoRouteListener : IDisposable
{
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Configuration cfg;
    private readonly AiClient client;
    private readonly ChatPipe pipe;

    public AutoRouteListener(IChatGui chat, IPluginLog log, Configuration cfg, AiClient client, ChatPipe pipe)
    {
        this.chat = chat;
        this.log = log;
        this.cfg = cfg;
        this.client = client;
        this.pipe = pipe;

        chat.ChatMessage += OnChatMessage;
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        throw new NotImplementedException();
    }

    private void OnChatMessage(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        try
        {
            // Only handle Party and Say for now
            var route = type switch
            {
                XivChatType.Party => ChatRoute.Party,
                XivChatType.Say => ChatRoute.Say,
                _ => (ChatRoute?)null
            };
            if (route == null) return;

            // Load per-route settings
            if (!IsListenerEnabled(route.Value)) return;

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
        }
    }

    private async Task RespondStreamAsync(ChatRoute route, string prompt, string caller)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

            // Optional echo of the caller prompt, routed back to same channel
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

            // Header for first streamed line
            var ai = string.IsNullOrWhiteSpace(cfg.AiDisplayName) ? "AI Nunu" : cfg.AiDisplayName;
            var replyFmt = GetAiReplyFormat(route);
            var header = (replyFmt ?? "{ai} -> {caller}: {reply}")
                .Replace("{ai}", ai)
                .Replace("{caller}", caller)
                .Replace("{reply}", string.Empty);

            // Stream tokens back to SAME channel
            var tokens = client.ChatStreamAsync(new List<ChatMessage>(), $"Caller: {caller}\n\n{prompt}", cts.Token);
            await pipe.SendStreamingToAsync(route, tokens, header, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* quiet */ }
        catch (Exception ex)
        {
            log.Error(ex, "AutoRouteListener RespondStreamAsync error");
        }
    }

    // ----- per-route config helpers -----
    private bool IsListenerEnabled(ChatRoute route) =>
        route == ChatRoute.Party ? cfg.EnablePartyListener :
        route == ChatRoute.Say ? cfg.EnableSayListener : false;

    private string GetTrigger(ChatRoute route) =>
        route == ChatRoute.Party ? (cfg.PartyTrigger ?? "!AI Nunu") :
        route == ChatRoute.Say ? (cfg.SayTrigger ?? "!AI Nunu") : "!AI Nunu";

    private bool IsWhitelisted(ChatRoute route, string caller)
    {
        var list = route == ChatRoute.Party ? cfg.PartyWhitelist : cfg.SayWhitelist;
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

    // ----- utils -----
    private static string NormalizeName(string name)
    {
        var n = name ?? string.Empty;
        var at = n.IndexOf('@'); if (at >= 0) n = n[..at];
        return string.Join(' ', n.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
    }

    public void Dispose() => chat.ChatMessage -= OnChatMessage;
}
