// SPDX-License-Identifier: MIT
// AiCompanionPlugin - ChatTwoBridge.cs

#nullable enable
using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace AiCompanionPlugin
{
    public sealed class ChatTwoBridge : IDisposable
    {
        private readonly IDalamudPluginInterface pluginInterface;

        private readonly ICallGateSubscriber<string, bool>? canSend;
        private readonly ICallGateSubscriber<string, string, bool>? sendChannel;
        private readonly ICallGateSubscriber<string, bool>? sendSay;
        private readonly ICallGateSubscriber<string, bool>? sendParty;

        public ChatTwoBridge(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface ?? throw new ArgumentNullException(nameof(pluginInterface));
            TryGet("ChatTwo.CanSend", out canSend);
            TryGet("ChatTwo.Send", out sendChannel);
            TryGet("ChatTwo.SendSay", out sendSay);
            TryGet("ChatTwo.SendParty", out sendParty);
        }

        public bool IsAvailable => sendChannel is not null || sendSay is not null || sendParty is not null;

        public bool CanSend(string channel)
        {
            channel = (channel ?? string.Empty).Trim().ToLowerInvariant();
            if (canSend is not null)
            {
                try { return canSend.InvokeFunc(channel); } catch { }
            }
            return channel switch
            {
                "say" => (sendSay is not null) || (sendChannel is not null),
                "party" => (sendParty is not null) || (sendChannel is not null),
                _ => sendChannel is not null
            };
        }

        public bool Send(string channel, string message)
        {
            channel = (channel ?? string.Empty).Trim().ToLowerInvariant();
            message = message ?? string.Empty;

            try
            {
                if (channel == "say" && sendSay is not null) return sendSay.InvokeFunc(message);
                if (channel == "party" && sendParty is not null) return sendParty.InvokeFunc(message);
                if (sendChannel is not null) return sendChannel.InvokeFunc(channel, message);
            }
            catch { }
            return false;
        }

        private void TryGet<T1, TResult>(string name, out ICallGateSubscriber<T1, TResult>? sub)
        {
            try { sub = pluginInterface.GetIpcSubscriber<T1, TResult>(name); }
            catch { sub = null; }
        }

        private void TryGet<T1, T2, TResult>(string name, out ICallGateSubscriber<T1, T2, TResult>? sub)
        {
            try { sub = pluginInterface.GetIpcSubscriber<T1, T2, TResult>(name); }
            catch { sub = null; }
        }

        public void Dispose() { }
    }
}
