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
        private IChatRouter? router;

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

            // UI: wire persona preview + test send delegates
            settingsWindow = new SettingsWindow(
                config,
                SaveConfigSafe,
                getPersonaPreview: () => persona.GetSystemPrompt(),
                sendTestSay: (s) => TrySendChannel(XivChatType.Say, s),
                sendTestParty: (s) => TrySendChannel(XivChatType.Party, s)
            );

            // pass a tiny shim to ChatWindow to send directly to channels
            chatWindow = new ChatWindow(
                config,
                GetHistory,
                SendUserMessage,
                trySendChannel: (type, text) =>
                {
                    // 1 => Say, 2 => Party (avoid bringing in XivChatType into ChatWindow signature)
                    return type switch
                    {
                        2 => TrySendChannel(XivChatType.Party, text),
                        _ => TrySendChannel(XivChatType.Say, text),
                    };
                });

            // hook UI
            pi.UiBuilder.Draw += DrawUI;
            pi.UiBuilder.OpenConfigUi += OpenConfigUi;

            TryOpenOnLoad();
            TryLogInfo("Constructed. Systems will finalize on first draw.");
        }

        private bool TrySendChannel(XivChatType party, object text) => throw new NotImplementedException();

        private void LateInit()
        {
            if (nativePipe == null)
                nativePipe = new NativeChatPipe(config, CommandManager, Log);
            if (chatTwo == null)
                chatTwo = new ChatTwoBridge(PluginInterface, Log);
            if (router == null && ChatGui != null)
                router = new IChatRouter(config, ChatGui, Log, OnIncomingTrigger);
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

            // Build prompt with persona (effective system prompt)
            var systemPrompt = persona.GetSystemPrompt();
            // TODO: Call your AI client with (systemPrompt, history, message) and capture reply.

            // Placeholder reply for flow proof:
            var reply = $"♪ {config.AiDisplayName}: {message}";
            history.Add(("assistant", reply));

            // Also post assistant reply to chat if toggled
            if (config.PostAssistantToSay)
                TrySendChannel(XivChatType.Say, reply);
            if (config.PostAssistantToParty)
                TrySendChannel(XivChatType.Party, reply);
        }

        // Incoming chat trigger -> send to model, send a response to chat
        private void OnIncomingTrigger(string payload, XivChatType sourceType, string from)
        {
            TryLogInfo($"Trigger from [{from}] via {sourceType}: {payload}");

            // TODO: Use AI client; placeholder uses persona name
            var who = string.IsNullOrWhiteSpace(config.AiDisplayName) ? "Nunu" : config.AiDisplayName;
            var reply = $"{who}: {payload} — WAH~";

            // Always reply into the same channel when triggered
            if (!TrySendChannel(sourceType, reply))
                ChatGui?.Print($"[AI Companion] {reply}");

            // Mirror into UI history too
            history.Add(("assistant", reply));
        }

        internal bool TrySendChannel(XivChatType type, string text)
        {
            var sent = false;

            if (type == XivChatType.Party)
            {
                sent = chatTwo?.TrySendParty(text) == true || nativePipe?.TrySendParty(text) == true;
            }
            else // treat say/shout/yell as say
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
}
