// SPDX-License-Identifier: MIT
// AiCompanionPlugin - Plugin.cs
//
// Wires UI + persona + chat routing + real AI backend call.
// Slash commands:
//   /nunuwin  -> toggle Chat window
//   /nunucfg  -> open Settings window

#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
        private AiClient? ai;

        // history for ChatWindow
        private readonly List<(string role, string content)> history = new();
        private readonly object historyLock = new();

        private bool commandsRegistered;

        // Cancellation for in-flight calls
        private CancellationTokenSource? callCts;

        public Plugin(IDalamudPluginInterface pi)
        {
            var cfg = pi.GetPluginConfig() as Configuration ?? new Configuration();
            cfg.Initialize(pi);
            config = cfg;

            persona = new PersonaManager(config);

            // seed
            AppendHistory(("system", "Nunu tunes her voidbound lute. WAH!"));

            // UI: wire persona preview + test send delegates
            settingsWindow = new SettingsWindow(
                config,
                SaveConfigSafe,
                getPersonaPreview: () => persona.GetSystemPrompt(),
                sendTestSay: (s) => TrySendChannel(XivChatType.Say, s),
                sendTestParty: (s) => TrySendChannel(XivChatType.Party, s)
            );

            // pass a shim to ChatWindow to send to channels
            chatWindow = new ChatWindow(
                config,
                GetHistory,
                SendUserMessage,
                trySendChannel: (type, text) =>
                {
                    // 1 => Say, 2 => Party
                    return type switch
                    {
                        2 => TrySendChannel(XivChatType.Party, text),
                        _ => TrySendChannel(XivChatType.Say, text),
                    };
                });

            // hook UI (event subscriptions return void; do not assign)
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
                router = new IChatRouter(config, ChatGui, Log, OnIncomingTrigger);
            if (ai == null)
            {
                try
                {
                    ai = new AiClient(config, Log);
                }
                catch (Exception ex)
                {
                    TryLogError(ex, "Failed to initialize AI client. Check BackendBaseUrl.");
                }
            }
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

            // AddHandler returns void; do not assign to a variable
            CommandManager.AddHandler("/nunuwin", new CommandInfo(OnCmdNunuWin)
            {
                HelpMessage = "Toggle AI Companion chat window."
            });

            CommandManager.AddHandler("/nunucfg", new CommandInfo(OnCmdNunuCfg)
            {
                HelpMessage = "Open AI Companion settings window."
            });

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
        private IReadOnlyList<(string role, string content)> GetHistory()
        {
            lock (historyLock)
                return history.ToArray(); // snapshot
        }

        private void AppendHistory((string role, string content) item)
        {
            lock (historyLock) history.Add(item);
        }

        private void SendUserMessage(string message)
        {
            AppendHistory(("user", message));
            TryLogInfo($"User -> {message}");

            // cancel previous call if any
            callCts?.Cancel();
            callCts = new CancellationTokenSource();

            _ = StartCompletionAsync(message, callCts.Token);
        }

        private async Task StartCompletionAsync(string message, CancellationToken ct)
        {
            string replyText;
            try
            {
                if (ai == null)
                {
                    AppendHistory(("assistant", "(AI backend not initialized — check BackendBaseUrl)"));
                    return;
                }

                var sys = persona.GetSystemPrompt();
                var prior = GetHistory();
                replyText = await ai.GetChatCompletionAsync(sys, prior, message, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(replyText))
                    replyText = "(no content)";
            }
            catch (OperationCanceledException)
            {
                TryLogInfo("AI call canceled.");
                return;
            }
            catch (Exception ex)
            {
                var msg = $"AI error: {ex.Message}";
                TryLogError(ex, "Completion failed");
                ChatGui?.PrintError($"[AI Companion] {msg}");
                AppendHistory(("assistant", msg));
                return;
            }

            AppendHistory(("assistant", replyText));

            // Also post assistant reply to chat if toggled
            if (config.PostAssistantToSay)
                TrySendChannel(XivChatType.Say, replyText);
            if (config.PostAssistantToParty)
                TrySendChannel(XivChatType.Party, replyText);
        }

        // Incoming chat trigger -> send to model, send a response to chat
        private void OnIncomingTrigger(string payload, XivChatType sourceType, string from)
        {
            TryLogInfo($"Trigger from [{from}] via {sourceType}: {payload}");

            // Route to AI using the payload as message
            callCts?.Cancel();
            callCts = new CancellationTokenSource();
            _ = StartTriggeredReplyAsync(payload, sourceType, callCts.Token);
        }

        private async Task StartTriggeredReplyAsync(string input, XivChatType target, CancellationToken ct)
        {
            string replyText;
            try
            {
                if (ai == null)
                {
                    ChatGui?.PrintError("[AI Companion] AI backend not initialized — check BackendBaseUrl");
                    return;
                }

                var sys = persona.GetSystemPrompt();
                var prior = GetHistory();
                replyText = await ai.GetChatCompletionAsync(sys, prior, input, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(replyText))
                    replyText = "(no content)";
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                TryLogError(ex, "Triggered reply failed");
                ChatGui?.PrintError($"[AI Companion] AI error: {ex.Message}");
                return;
            }

            // Post to same channel as trigger
            if (!TrySendChannel(target, replyText))
                ChatGui?.Print($"[AI Companion] {replyText}");

            AppendHistory(("assistant", replyText));
        }

        internal bool TrySendChannel(XivChatType type, string text)
        {
            // Route selection based on config.ChatPostingMode
            bool sent = false;

            switch (config.ChatPostingMode)
            {
                case ChatPostingMode.ChatTwoIpc:
                    sent = (type == XivChatType.Party)
                        ? chatTwo?.TrySendParty(text) == true
                        : chatTwo?.TrySendSay(text) == true;
                    break;

                case ChatPostingMode.Native:
                    sent = (type == XivChatType.Party)
                        ? nativePipe?.TrySendParty(text) == true
                        : nativePipe?.TrySendSay(text) == true;
                    break;

                case ChatPostingMode.Auto:
                default:
                    if (type == XivChatType.Party)
                        sent = (chatTwo?.TrySendParty(text) == true) || (nativePipe?.TrySendParty(text) == true);
                    else
                        sent = (chatTwo?.TrySendSay(text) == true) || (nativePipe?.TrySendSay(text) == true);
                    break;
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
                ai?.Dispose();

                if (commandsRegistered && CommandManager is not null)
                {
                    // RemoveHandler returns void; do not assign
                    CommandManager.RemoveHandler("/nunuwin");
                    CommandManager.RemoveHandler("/nunucfg");
                }
            }
            catch { }

            TryLogInfo("Disposed.");
        }
    }
}
