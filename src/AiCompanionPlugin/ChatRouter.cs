// SPDX-License-Identifier: MIT
// AiCompanionPlugin - ChatRouter.cs
//
// Listens to chat and fires a callback when trigger phrases match and pass whitelist rules.

#nullable enable
using System;
using System.Collections.Generic;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin
{
    public sealed class IChatRouter : IDisposable
    {
        private readonly Configuration config;
        private readonly IChatGui chatGui;
        private readonly IPluginLog? log;
        private readonly Action<string, XivChatType, string> onTriggered;

        public IChatRouter(Configuration config, IChatGui chatGui, IPluginLog? log,
            Action<string, XivChatType, string> onTriggered)
        {
            this.config = config;
            this.chatGui = chatGui;
            this.log = log;
            this.onTriggered = onTriggered;

            chatGui.ChatMessage += OnChatMessage;
        }

        public void Dispose()
        {
            try { chatGui.ChatMessage -= OnChatMessage; } catch { }
        }

        private void OnChatMessage(XivChatType type, int senderId, ref SeString sender, ref SeString message, ref bool isHandled)
        {
            try
            {
                var text = message.TextValue ?? string.Empty;
                var from = sender.TextValue ?? string.Empty;

                // Check channels we care about
                var isSay = type == XivChatType.Say || type == XivChatType.Shout || type == XivChatType.Yell;
                var isParty = type == XivChatType.Party;

                string? trigger = null;
                if (isSay && !string.IsNullOrWhiteSpace(config.SayTrigger) && text.StartsWith(config.SayTrigger, StringComparison.OrdinalIgnoreCase))
                    trigger = config.SayTrigger;
                else if (isParty && !string.IsNullOrWhiteSpace(config.PartyTrigger) && text.StartsWith(config.PartyTrigger, StringComparison.OrdinalIgnoreCase))
                    trigger = config.PartyTrigger;

                if (trigger is null) return;

                // Whitelist if required
                if (config.RequireWhitelist)
                {
                    var wl = isParty ? config.PartyWhitelist : config.SayWhitelist;
                    if (!IsWhitelisted(wl, from))
                        return;
                }

                // Strip trigger and trim
                var payload = text[trigger.Length..].TrimStart();
                if (string.IsNullOrEmpty(payload)) return;

                onTriggered(payload, isParty ? XivChatType.Party : XivChatType.Say, from);
            }
            catch (Exception ex)
            {
                try { log?.Warning(ex, "ChatRouter error"); } catch { }
            }
        }

        private static bool IsWhitelisted(List<string> list, string name)
        {
            if (list.Count == 0) return false;
            foreach (var entry in list)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                if (string.Equals(entry.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
