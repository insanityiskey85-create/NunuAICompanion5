// SPDX-License-Identifier: MIT
// AiCompanionPlugin - AutoRouteListener.cs

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace AiCompanionPlugin
{
    /// <summary>
    /// Listens for incoming chat lines and conditionally routes them to the AI
    /// based on channel-specific triggers and optional whitelists.
    /// 
    /// This class is deliberately decoupled from a specific Plugin class:
    /// pass in a router delegate to avoid depending on Plugin.RouteIncoming.
    /// </summary>
    public sealed class AutoRouteListener
    {
        private readonly Configuration config;

        /// <summary>
        /// Signature: router(channel, sender, message)
        /// channel: e.g., "say" or "party"
        /// sender: character name
        /// message: message content (raw)
        /// </summary>
        private readonly Action<string, string, string> router;

        public AutoRouteListener(Configuration config, Action<string, string, string> router)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.router = router ?? throw new ArgumentNullException(nameof(router));
        }

        /// <summary>
        /// Feed chat messages into this from your ChatGui event handler.
        /// Example channel values: "say", "party", etc.
        /// </summary>
        public void OnChatMessage(string channel, string sender, string message)
        {
            if (string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(message))
                return;

            channel = channel.Trim().ToLowerInvariant();
            sender = sender?.Trim() ?? string.Empty;
            message = message.Trim();

            switch (channel)
            {
                case "say":
                    if (MatchesTrigger(message, config.SayTrigger) &&
                        PassesWhitelist(sender, config.RequireWhitelist, config.SayWhitelist))
                    {
                        router("say", sender, message);
                    }
                    break;

                case "party":
                    if (MatchesTrigger(message, config.PartyTrigger) &&
                        PassesWhitelist(sender, config.RequireWhitelist, config.PartyWhitelist))
                    {
                        router("party", sender, message);
                    }
                    break;

                default:
                    // Not handled; ignore silently
                    break;
            }
        }

        private static bool MatchesTrigger(string message, string trigger)
        {
            // Empty trigger means "no special token required" (always match).
            if (string.IsNullOrWhiteSpace(trigger))
                return true;

            return message.IndexOf(trigger, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool PassesWhitelist(string sender, bool requireWhitelist, List<string> whitelist)
        {
            if (!requireWhitelist)
                return true;

            if (string.IsNullOrWhiteSpace(sender))
                return false;

            if (whitelist == null || whitelist.Count == 0)
                return false;

            return whitelist.Any(n =>
                n != null &&
                sender.Equals(n.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }
}
