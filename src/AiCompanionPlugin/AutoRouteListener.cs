using System;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

// Small guard that decides if a message should be routed to AI based on triggers + whitelist.
// This does NOT post to channels; it lets your higher layer decide what to do.
public sealed class AutoRouteListener : IDisposable
{
    private readonly IPluginLog log;
    private readonly IChatGui chat;
    private readonly Configuration config;

    public AutoRouteListener(IPluginLog log, IChatGui chat, Configuration config)
    {
        this.log = log;
        this.chat = chat;
        this.config = config;
        chat.CheckMessageHandled += OnChatMessage; // API 13 signature
        log.Info("[AutoRoute] listener armed");
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        try
        {
            var text = message.TextValue ?? string.Empty;
            var name = sender.TextValue ?? string.Empty;

            // normalize
            var trimmed = text.Trim();
            var tSay = config.SayTrigger?.Trim();
            var tParty = config.PartyTrigger?.Trim();

            bool isSay = type == XivChatType.Say;
            bool isParty = type == XivChatType.Party;

            if (config.DebugChatTap)
            {
                chat.Print($"[ChatTap #{timestamp}] {(isSay ? "Say" : isParty ? "Party" : type.ToString())} @{timestamp % 1000} Sender='{name}' Msg='{trimmed}'");
            }

            if (isSay && config.EnableSayListener)
            {
                if (!string.IsNullOrEmpty(tSay) && trimmed.StartsWith(tSay, StringComparison.OrdinalIgnoreCase))
                {
                    if (!config.RequireWhitelist || config.SayWhitelist.Contains(name))
                    {
                        // Just echo to debug; your higher layer (SayListener) will handle full AI flow.
                        if (config.DebugChatTap)
                            chat.Print($"[AI Companion:Say] → {name} → {trimmed.Substring(tSay.Length).Trim()}");
                    }
                }
            }

            if (isParty && config.EnablePartyListener)
            {
                if (!string.IsNullOrEmpty(tParty) && trimmed.StartsWith(tParty, StringComparison.OrdinalIgnoreCase))
                {
                    if (!config.RequireWhitelist || config.PartyWhitelist.Contains(name))
                    {
                        if (config.DebugChatTap)
                            chat.Print($"[AI Companion:Party] → {name} → {trimmed.Substring(tParty.Length).Trim()}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "[AutoRoute] OnChatMessage failed");
        }
    }

    public void Dispose()
    {
        chat.CheckMessageHandled -= OnChatMessage;
    }
}
