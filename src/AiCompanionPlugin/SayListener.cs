// SPDX-License-Identifier: MIT
// AiCompanionPlugin - SayListener.cs
//
// Listens for /say triggers and relays to the AI + chat pipeline.
// Visibility set to internal to match potentially-internal AiClient and avoid CS0051.

#nullable enable
using System;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin
{
    internal sealed class SayListener : IDisposable
    {
        private readonly IPluginLog? log;
        private readonly IChatGui? chatGui;
        private readonly Configuration config;
        private readonly AiCompanionPlugin.AiClient ai;
        private readonly ChatPipe chatPipe;

        private bool subscribed;

        /// <summary>
        /// INTERNAL ctor so parameter types (AiClient, etc.) are not more private than the method.
        /// Fully-qualify AiClient to dodge any namespace collisions.
        /// </summary>
        internal SayListener(
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

            // Event subscription returns void — do NOT assign it to a variable.
            chatGui.ChatMessage += OnChatMessage;
            subscribed = true;

            log?.Info("[SayListener] Subscribed to ChatMessage.");
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
                if (type != XivChatType.Say)
                    return;

                var text = message.TextValue ?? string.Empty;
                var trigger = config.SayTrigger ?? string.Empty;
                if (string.IsNullOrWhiteSpace(trigger))
                    return;

                if (!text.StartsWith(trigger, StringComparison.OrdinalIgnoreCase))
                    return;

                // strip trigger and trim
                var payload = text.Substring(trigger.Length).Trim();
                if (payload.Length == 0)
                    return;

                // Hand over to ChatPipe (which will route to AI and post back using configured mode)
                chatPipe.EnqueueUserPromptFromChat(payload, XivChatType.Say);

                // If you want to swallow the original message, uncomment:
                // isHandled = true;
            }
            catch (Exception ex)
            {
                log?.Error(ex, "[SayListener] Error handling /say trigger.");
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
                    log?.Info("[SayListener] Unsubscribed from ChatMessage.");
                }
            }
            catch
            {
                // swallow on dispose
            }
        }
    }
}
