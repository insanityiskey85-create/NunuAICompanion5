// SPDX-License-Identifier: MIT
// AiCompanionPlugin - ChatPipe.cs
//
// High-level router that uses OutboundDispatcher + NativeChatPipe (and ChatTwo if available).

#nullable enable
using System;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin
{
    public sealed class ChatPipe : IDisposable
    {
        private readonly Configuration config;
        private readonly IPluginLog log;
        private readonly NativeChatPipe native;
        private readonly OutboundDispatcher dispatcher;
        private readonly ChatTwoBridge? chatTwo;

        public ChatPipe(Configuration config, IPluginLog log, IChatGui chat, IFramework framework, ChatTwoBridge? chatTwo = null)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
            this.native = new NativeChatPipe(config, chat);
            this.dispatcher = new OutboundDispatcher(framework, native);
            this.chatTwo = chatTwo;
        }

        public void SendSay(string text)
        {
            if (chatTwo is not null && chatTwo.IsAvailable && chatTwo.CanSend("say"))
                dispatcher.EnqueueRaw("/say " + (text ?? string.Empty));
            else
                dispatcher.EnqueueSay(text ?? string.Empty);
        }

        public void SendParty(string text)
        {
            if (chatTwo is not null && chatTwo.IsAvailable && chatTwo.CanSend("party"))
                dispatcher.EnqueueRaw("/p " + (text ?? string.Empty));
            else
                dispatcher.EnqueueParty(text ?? string.Empty);
        }

        public void SendRaw(string line) => dispatcher.EnqueueRaw(line ?? string.Empty);

        public void Dispose() => dispatcher.Dispose();
        internal void EnqueueUserPromptFromChat(string payload, XivChatType say) => throw new NotImplementedException();
    }
}
