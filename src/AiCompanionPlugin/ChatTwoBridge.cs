// SPDX-License-Identifier: MIT
// AiCompanionPlugin - ChatTwoBridge.cs
//
// Optional IPC bridge to ChatTwo. If not present, calls will return false.

#nullable enable
using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin
{
    public sealed class ChatTwoBridge
    {
        private readonly IPluginLog? log;
        private readonly ICallGateSubscriber<string, bool>? sendText;     // e.g., "ChatTwo.SendMessage"
        private readonly ICallGateSubscriber<string, bool>? sendSay;      // optional "ChatTwo.SendSay"
        private readonly ICallGateSubscriber<string, bool>? sendParty;    // optional "ChatTwo.SendParty"

        public ChatTwoBridge(IDalamudPluginInterface pi, IPluginLog? log)
        {
            this.log = log;

            try
            {
                // Common generic "send text" IPC some ChatTwo forks expose
                sendText = pi.GetIpcSubscriber<string, bool>("ChatTwo.SendMessage");
            }
            catch { /* missing is fine */ }

            try { sendSay = pi.GetIpcSubscriber<string, bool>("ChatTwo.SendSay"); } catch { }
            try { sendParty = pi.GetIpcSubscriber<string, bool>("ChatTwo.SendParty"); } catch { }
        }

        public bool TrySendSay(string text)
        {
            try
            {
                if (sendSay != null) return sendSay.InvokeFunc(text);
                if (sendText != null) return sendText.InvokeFunc($"/say {text}");
            }
            catch (Exception ex) { try { log?.Warning(ex, "ChatTwo IPC /say failed"); } catch { } }
            return false;
        }

        public bool TrySendParty(string text)
        {
            try
            {
                if (sendParty != null) return sendParty.InvokeFunc(text);
                if (sendText != null) return sendText.InvokeFunc($"/p {text}");
            }
            catch (Exception ex) { try { log?.Warning(ex, "ChatTwo IPC /p failed"); } catch { } }
            return false;
        }
    }
}
