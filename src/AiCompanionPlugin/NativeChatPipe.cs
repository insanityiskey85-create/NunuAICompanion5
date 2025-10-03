// File: src/AiCompanionPlugin/NativeChatPipe.cs
#nullable enable
using System;
using Dalamud.Plugin.Services;                     // IPluginLog
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace AiCompanionPlugin
{
    /// <summary>
    /// Low-level chat dispatch via client structs.
    /// Only used when ChatTwo/command manager routes are unavailable.
    /// </summary>
    internal sealed unsafe class NativeChatPipe
    {
        private readonly IPluginLog log;

        public NativeChatPipe(IPluginLog log)
        {
            this.log = log;
        }

        /// <summary>
        /// Execute a raw chat command (e.g., "/say hello").
        /// Returns true on success.
        /// </summary>
        public bool TryExecuteCommand(string command)
        {
            try
            {
                var shell = RaptureShellModule.Instance();
                if (shell == null)
                {
                    log.Warning("[NativeChatPipe] RaptureShellModule.Instance() was null.");
                    return false;
                }

                // Utf8String.FromString allocates a temporary Utf8 buffer for the game
                using var u = Utf8String.FromString(command);
                if (u == null)
                {
                    log.Warning("[NativeChatPipe] Utf8String.FromString returned null for: {Command}", command);
                    return false;
                }

                shell->ExecuteCommand(u.StringPtr);
                log.Information("[NativeChatPipe] Dispatched command: {Command}", command);
                return true;
            }
            catch (Exception ex)
            {
                log.Error(ex, "[NativeChatPipe] Exception while executing command: {Command}", command);
                return false;
            }
        }

        public bool TrySay(string text)
            => TryExecuteCommand($"/say {text}");

        public bool TryParty(string text)
            => TryExecuteCommand($"/p {text}");

        /// <summary>
        /// Optional helper: Sets the chat input box contents (does not send).
        /// Useful if you want the user to confirm.
        /// </summary>
        public bool TryStageInChatBox(string text)
        {
            try
            {
                var atk = RaptureAtkModule.Instance();
                if (atk == null)
                {
                    log.Warning("[NativeChatPipe] RaptureAtkModule.Instance() was null.");
                    return false;
                }

                var uiModule = UIModule.Instance();
                if (uiModule == null)
                {
                    log.Warning("[NativeChatPipe] UIModule.Instance() was null.");
                    return false;
                }

                using var u = Utf8String.FromString(text);
                if (u == null)
                {
                    log.Warning("[NativeChatPipe] Utf8String.FromString returned null for stage text.");
                    return false;
                }

                // This path varies by CS version/UI state; we stick to ExecuteCommand for sending.
                // Keeping this as a staging helper only:
                atk->ChatInput->SetText(u.StringPtr);
                log.Information("[NativeChatPipe] Staged text in chat input.");
                return true;
            }
            catch (Exception ex)
            {
                log.Error(ex, "[NativeChatPipe] Exception while staging chat text.");
                return false;
            }
        }
    }
}
