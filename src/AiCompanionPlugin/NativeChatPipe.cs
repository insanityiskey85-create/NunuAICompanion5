// SPDX-License-Identifier: MIT
// AiCompanionPlugin - NativeChatPipe.cs
//
// Sends messages to actual chat channels via ICommandManager.ProcessCommand.
// Falls back gracefully if CommandManager is missing. Applies ASCII/length shaping.

#nullable enable
using System;
using System.Text;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin
{
    public sealed class NativeChatPipe
    {
        private readonly Configuration config;
        private readonly ICommandManager? commands;
        private readonly IPluginLog? log;

        public NativeChatPipe(Configuration config, ICommandManager? commandManager, IPluginLog? log)
        {
            this.config = config;
            this.commands = commandManager;
            this.log = log;
        }

        public NativeChatPipe(Configuration config, IChatGui chat)
        {
            this.config = config;
        }

        public bool TrySendSay(string message)
            => TrySendChatCommand("/say ", message);

        public bool TrySendParty(string message)
            => TrySendChatCommand("/p ", message);

        private bool TrySendChatCommand(string prefix, string message)
        {
            try
            {
                if (commands is null) return false;

                var shaped = Shape(message);
                if (string.IsNullOrWhiteSpace(shaped)) return false;

                var cmd = $"{prefix}{shaped}";
                commands.ProcessCommand(cmd);
                return true;
            }
            catch (Exception ex)
            {
                try { log?.Warning(ex, "NativeChatPipe failed {Prefix}", prefix.Trim()); } catch { }
                return false;
            }
        }

        private string Shape(string message)
        {
            message ??= string.Empty;

            if (config.AsciiSafe)
            {
                var sb = new StringBuilder(message.Length);
                foreach (var ch in message)
                {
                    if (ch <= 0x7F) sb.Append(ch);
                    else sb.Append('?');
                }
                message = sb.ToString();
            }

            var max = Math.Clamp(config.MaxPostLength, 1, 500);
            if (message.Length > max)
                message = message[..max];

            return message.Trim();
        }

        internal bool SendSay(string text) => throw new NotImplementedException();
        internal bool SendParty(string text) => throw new NotImplementedException();
        internal bool SendRaw(string line) => throw new NotImplementedException();
    }
}
