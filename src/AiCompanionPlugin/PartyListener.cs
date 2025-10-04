// SPDX-License-Identifier: MIT
// AiCompanionPlugin - PartyListener.cs
//
// Listens for /party triggers and relays to the AI + chat pipeline.
// Visibility set to internal to match potentially-internal AiClient and avoid CS0051.

#nullable enable
using System;
using System.Linq;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin
{
    internal sealed class PartyListener : IDisposable
    {
        private readonly IPluginLog? log;
        private readonly IChatGui? chatGui;
        private readonly Configuration config;
        private readonly AiCompanionPlugin.AiClient ai;
        private readonly ChatPipe chatPipe;

        private bool subscribed;

        /// <summary>
        /// INTERNAL ctor so parameter types (AiClient, ChatPipe) are not more private than the method.
        /// </summary>
        internal PartyListener(
            IPluginLog log,
            IChatGui chatGui,
            Configuration config,
            AiCompanionPlugin.AiClient ai,
            ChatPipe chat)
        {
            this.log = log;
            this.chatGui = chatGui;
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.ai = ai ?? throw new ArgumentNullException(nameof(ai));
            this.chatPipe = chat ?? throw new ArgumentNullException(nameof(chat));

            TrySubscribe();
        }

        private void TrySubscribe()
        {
            if (chatGui == null || subscribed)
                return;

            // Event subscription returns void — do NOT assign it.
            chatGui.ChatMessage += OnChatMessage;
            subscribed = true;

            log?.Info("[PartyListener] Subscribed to ChatMessage.");
        }

        private void OnChatMessage(
            XivChatType type,
            int timestamp,
            ref Dalamud.Game.Text.SeStringHandling.SeString sender,
            ref Dalamud.Game.Text.SeStringHandling.SeString message,
            ref bool isHandled)
        {
            try
            {
                if (type != XivChatType.Party)
                    return;

                var trigger = config.PartyTrigger ?? string.Empty;
                if (string.IsNullOrWhiteSpace(trigger))
                    return;

                var text = message.TextValue ?? string.Empty;
                if (!text.StartsWith(trigger, StringComparison.OrdinalIgnoreCase))
                    return;

                // Whitelist enforcement (optional)
                if (config.RequireWhitelist)
                {
                    var name = (sender.TextValue ?? string.Empty).Trim();
                    var allowed = config.PartyWhitelist.Any(w =>
                        !string.IsNullOrWhiteSpace(w) &&
                        string.Equals(w.Trim(), name, StringComparison.OrdinalIgnoreCase));

                    if (!allowed)
                    {
                        log?.Info($"[PartyListener] Blocked by whitelist: '{name}'");
                        return;
                    }
                }

                // strip trigger → payload
                var payload = text.Substring(trigger.Length).Trim();
                if (payload.Length == 0)
                    return;

                // Hand over to ChatPipe (which will route to AI and post back using configured mode)
                chatPipe.EnqueueUserPromptFromChat(payload, XivChatType.Party);

                // If you want to swallow the original message, uncomment:
                // isHandled = true;
            }
            catch (Exception ex)
            {
                log?.Error(ex, "[PartyListener] Error handling /party trigger.");
            }
        }

        public void Dispose()
        {
            try
            {
                if (chatGui != null && subscribed)
                {
                    chatGui.ChatMessage -= OnChatMessage;
                    subscribed = false;
                    log?.Info("[PartyListener] Unsubscribed from ChatMessage.");
                }
            }
            catch
            {
                // swallow on dispose
            }
        }
    }
}
