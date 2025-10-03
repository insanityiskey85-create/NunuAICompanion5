// SPDX-License-Identifier: MIT
// AiCompanionPlugin - Plugin.cs
//
// Wires UI + persona + chat routing + native/IPC sends.
// Adds slash commands:
//   /nunuwin  -> toggle Chat window
//   /nunucfg  -> open Settings window

#nullable enable
using System;
using System.Collections.Generic;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Command;
using Dalamud.Game.Text;

namespace AiCompanionPlugin
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "AI Companion";

        // Injected by Dalamud post-construction
        [PluginService] internal IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal IPluginLog? Log { get; private set; }
        [PluginService] internal IFramework? Framework { get; private set; }
        [PluginService] internal IChatGui? ChatGui { get; private set; }
        [PluginService] internal ICommandManager? CommandManager { get; private set; }

        // Windows
        private readonly SettingsWindow settingsWindow;
        private readonly ChatWindow chatWindow;

        // Systems
        private readonly Configuration config;
        private readonly PersonaManager persona;
        private NativeChatPipe? nativePipe;
        private ChatTwoBridge? chatTwo;
        private ChatRouter? router;

        // memory history for ChatWindow
        private readonly List<(string role, string content)> history = new();

        private bool commandsRegistered;

        public Plugin(IDalamudPluginInterface pi)
        {
            var cfg = pi.GetPluginConfig() as Configuration ?? new Configuration();
            cfg.Initialize(pi);
            config = cfg;

            persona = new PersonaManager(config);

            // seed
            history.Add(("system", "Nunu tunes her voidbound lute. WAH!"));

            // UI
            settingsWindow = new SettingsWindow(config, SaveConfigSafe);
            chatWindow = new ChatWindow(config, GetHistory, SendUserMessage);

            // hook UI
            pi.UiBuilder.Draw += DrawUI;
            pi.UiBuilder.OpenConfigUi += OpenConfigUi;

            TryOpenOnLoad();
            TryLogInfo("Constructed. Systems will finalize on first draw.");
        }

        private void LateInit()
        {
            if (nativePipe == null)
                nativePipe = new NativeChatPipe(config, CommandManager, Log);
            if (chatTwo == null)
                chatTwo = new ChatTwoBridge(PluginInterface, Log);
            if (router == null && ChatGui != null)
                router = new ChatRouter(config, ChatGui, Log, OnIncomingTrigger);
        }

        private void DrawUI()
        {
            LateInit();
            EnsureCommands();

            settingsWindow.Draw();
            chatWindow.Draw();
        }

        private void OpenConfigUi() => settingsWindow.IsOpen = true;

        private void TryOpenOnLoad()
        {
            try
            {
                if (config.OpenSettingsOnLoad) settingsWindow.IsOpen = true;
                if (config.OpenChatOnLoad) chatWindow.IsOpen = true;
            }
            catch { }
        }

        // Slash commands
        private void EnsureCommands()
        {
            if (commandsRegistered || CommandManager is null) return;

            CommandManager.AddHandler("/nunuwin", new CommandInfo(OnCmdNunuWin) { HelpMessage = "Toggle AI Companion chat window." });
            CommandManager.AddHandler("/nunucfg", new CommandInfo(OnCmdNunuCfg) { HelpMessage = "Open AI Companion settings window." });

            commandsRegistered = true;
            TryLogInfo("Commands: /nunuwin, /nunucfg");
        }

        private void OnCmdNunuWin(string command, string args)
        {
            chatWindow.IsOpen = !chatWindow.IsOpen;
            ChatGui?.Print($"[AI Companion] Chat window {(chatWindow.IsOpen ? "opened" : "closed")}.");
        }

        private void OnCmdNunuCfg(string command, string args)
        {
            settingsWindow.IsOpen = true;
            ChatGui?.Print("[AI Companion] Settings window opened.");
        }

        // ChatWindow plumbing
        private IReadOnlyList<(string role, string content)> GetHistory() => history;

        private void SendUserMessage(string message)
        {
            history.Add(("user", message));
            TryLogInfo($"User -> {message}");

            // Here is where you'd assemble the system prompt with persona:
            var systemPrompt = persona.GetSystemPrompt();
            // TODO: call your AI client using systemPrompt + history (not included here).

            // For now, echo to prove flow:
            var reply = $"♪ {config.AiDisplayName}: {message}";
            history.Add(("assistant", reply));
        }

        // Incoming chat trigger -> send to model, send a response to chat
        private void OnIncomingTrigger(string payload, XivChatType sourceType, string from)
        {
            TryLogInfo($"Trigger from [{from}] via {sourceType}: {payload}");

            // TODO: Use AI client; for now fabricate a short reply
            var reply = $"To {from}: {payload} — WAH~";

            // Route reply back to the same channel
            if (!TrySendChannel(sourceType, reply))
            {
                ChatGui?.Print($"[AI Companion] {reply}");
            }
        }

        private bool TrySendChannel(XivChatType type, string text)
        {
            var sent = false;

            // IPC first, then native
            if (type == XivChatType.Party)
            {
                sent = chatTwo?.TrySendParty(text) == true || nativePipe?.TrySendParty(text) == true;
            }
            else // say/shout/yell treated as say
            {
                sent = chatTwo?.TrySendSay(text) == true || nativePipe?.TrySendSay(text) == true;
            }

            return sent;
        }

        private void SaveConfigSafe()
        {
            try { config.Save(); TryLogInfo("Config saved."); }
            catch (Exception ex) { TryLogError(ex, "Save failed"); }
        }

        private void TryLogInfo(string msg) { try { Log?.Info(msg); } catch { } }
        private void TryLogError(Exception ex, string msg) { try { Log?.Error(ex, msg); } catch { } }

        public void Dispose()
        {
            try
            {
                PluginInterface.UiBuilder.Draw -= DrawUI;
                PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
                router?.Dispose();

                if (commandsRegistered && CommandManager is not null)
                {
                    CommandManager.RemoveHandler("/nunuwin");
                    CommandManager.RemoveHandler("/nunucfg");
                }
            }
            catch { }

            TryLogInfo("Disposed.");
        }
    }

    internal class ChatRouter
    {
        public ChatRouter(Configuration config, IChatGui chatGui, IPluginLog? log, Action<string, XivChatType, string> onIncomingTrigger)
        {
        }

        internal void Dispose() => throw new NotImplementedException();
    }
}
