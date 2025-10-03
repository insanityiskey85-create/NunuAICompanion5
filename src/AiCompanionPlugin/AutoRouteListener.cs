using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

/// <summary>
/// Listens to /say and /party, filters by trigger + whitelist, asks AI for a reply,
/// and forwards both the incoming message and the proposed reply into the ChatWindow.
/// Does NOT auto-post; buttons in ChatWindow allow manual routing.
/// </summary>
public sealed class AutoRouteListener : IDisposable
{
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Configuration cfg;
    private readonly AiClient ai;
    private readonly ChatPipe pipe;
    private readonly ChatWindow ui;

    private long tapCount;

    public AutoRouteListener(IChatGui chat, IPluginLog log, Configuration cfg, AiClient ai, ChatPipe pipe, ChatWindow ui)
    {
        this.chat = chat;
        this.log = log;
        this.cfg = cfg;
        this.ai = ai;
        this.pipe = pipe;
        this.ui = ui;

        chat.ChatMessage += OnChatMessage;
        log.Info("[AutoRoute] listener armed");
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        try
        {
            // Mirror any whitelisted SAY/PARTY messages (trigger-filtered)
            var route = RouteFrom(type);
            if (route == null) return;

            var rawSender = sender?.TextValue ?? string.Empty; // e.g., "Nunubu Nubu"
            var rawMessage = message?.TextValue ?? string.Empty;

            // basic pass-through: if no trigger configured, bail
            var trigger = route == ChatRoute.Party ? cfg.PartyTrigger : cfg.SayTrigger;
            if (string.IsNullOrWhiteSpace(trigger)) return;
            if (!rawMessage.Contains(trigger, StringComparison.Ordinal)) return;

            // whitelist
            if (cfg.RequireWhitelist)
            {
                var wl = route == ChatRoute.Party ? (cfg.PartyWhitelist ?? new()) : (cfg.SayWhitelist ?? new());
                if (wl.Count == 0 || !wl.Contains(rawSender, StringComparer.Ordinal))
                {
                    if (cfg.DebugChatTap)
                        chat.Print($"[AI Companion:{route}] Ignored (whitelist). Sender={rawSender}");
                    return;
                }
            }

            var id = System.Threading.Interlocked.Increment(ref tapCount);
            if (cfg.DebugChatTap)
                chat.Print($"[AI Companion:{route}] #{id} {rawSender}: {rawMessage}");

            // show incoming in the UI
            ui.NotifyIncoming(route.Value, rawSender, rawMessage);

            // Build a lightweight prompt to the AI (strip trigger)
            var prompt = rawMessage.Replace(trigger, "", StringComparison.Ordinal).Trim();
            if (string.IsNullOrWhiteSpace(prompt))
                prompt = "Respond succinctly to the caller.";

            // Ask the AI once (non-streaming here; window offers manual posting after)
            _ = Task.Run(async () =>
            {
                try
                {
                    var history = new List<ChatMessage>(); // no prior context; could be extended if desired
                    var reply = await ai.ChatOnceAsync(history, prompt, CancellationToken.None).ConfigureAwait(false);
                    ui.NotifyProposedReply(route.Value, rawSender, prompt, reply);
                }
                catch (Exception ex)
                {
                    log.Error(ex, "[AutoRoute] AI request failed");
                    ui.NotifyProposedReply(route.Value, rawSender, prompt, $"(error) {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            log.Error(ex, "[AutoRoute] OnChatMessage failed");
        }
    }

    private static ChatRoute? RouteFrom(XivChatType t) =>
        t switch
        {
            XivChatType.Say => ChatRoute.Say,
            XivChatType.Party => ChatRoute.Party,
            _ => null
        };

    public void Dispose()
    {
        chat.ChatMessage -= OnChatMessage;
    }
}
