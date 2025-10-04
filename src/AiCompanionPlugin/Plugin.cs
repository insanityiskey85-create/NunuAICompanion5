// SPDX-License-Identifier: MIT
// AiCompanionPlugin - Plugin.cs
//
// Wires UI + persona + chat routing + real AI backend call.
// Slash commands:
//   /nunuwin      -> toggle Chat window
//   /nunucfg      -> open Settings window
//   /nunuurl      -> set BackendBaseUrl (and rebuild client)
//   /nunumodel    -> set Model (and rebuild client)
//   /nunutls      -> set AllowInsecureTls on/off (and rebuild client)
//   /nunusave     -> save configuration
//   /nunureload   -> rebuild AI client from config

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
                    // 2 => Party, default => Say
                    return type == 2 ? TrySendChannel(XivChatType.Party, text)
                                     : TrySendChannel(XivChatType.Say, text);
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
                TryRebuildAiClient();
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

            // Core window toggles
            CommandManager.AddHandler("/nunuwin", new CommandInfo(OnCmdNunuWin)
            {
                HelpMessage = "Toggle AI Companion chat window."
            });

            CommandManager.AddHandler("/nunucfg", new CommandInfo(OnCmdNunuCfg)
            {
                HelpMessage = "Open AI Companion settings window."
            });

            // NEW: backend controls
            CommandManager.AddHandler("/nunuurl", new CommandInfo(OnCmdNunuUrl)
            {
                HelpMessage = "Set backend base URL, e.g. /nunuurl http://127.0.0.1:11434/"
            });

            CommandManager.AddHandler("/nunumodel", new CommandInfo(OnCmdNunuModel)
            {
                HelpMessage = "Set model id, e.g. /nunumodel qwen2.5:1.5b-instruct-q4_K_M"
            });

            CommandManager.AddHandler("/nunutls", new CommandInfo(OnCmdNunuTls)
            {
                HelpMessage = "Allow insecure TLS for dev HTTPS endpoints: /nunutls on or /nunutls off"
            });

            CommandManager.AddHandler("/nunusave", new CommandInfo(OnCmdNunuSave)
            {
                HelpMessage = "Save AI Companion configuration."
            });

            CommandManager.AddHandler("/nunureload", new CommandInfo(OnCmdNunuReload)
            {
                HelpMessage = "Rebuild AI client with current settings."
            });

            commandsRegistered = true;
            TryLogInfo("Commands: /nunuwin, /nunucfg, /nunuurl, /nunumodel, /nunutls, /nunusave, /nunureload");
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

        private void OnCmdNunuUrl(string command, string args)
        {
            var url = (args ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                ChatGui?.PrintError("[AI Companion] Usage: /nunuurl http://127.0.0.1:11434/ (or https://127.0.0.1:3001/)");
                return;
            }

            if (!url.EndsWith("/"))
                url += "/";

            config.BackendBaseUrl = url;
            SaveConfigSafe();
            TryRebuildAiClient();

            ChatGui?.Print($"[AI Companion] BackendBaseUrl set to {url}");
        }

        private void OnCmdNunuModel(string command, string args)
        {
            var model = (args ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(model))
            {
                ChatGui?.PrintError("[AI Companion] Usage: /nunumodel qwen2.5:1.5b-instruct-q4_K_M");
                return;
            }

            config.Model = model;
            SaveConfigSafe();
            TryRebuildAiClient();

            ChatGui?.Print($"[AI Companion] Model set to {model}");
        }

        private void OnCmdNunuTls(string command, string args)
        {
            var a = (args ?? string.Empty).Trim().ToLowerInvariant();
            bool? val = a switch
            {
                "on" or "true" or "1" => true,
                "off" or "false" or "0" => false,
                _ => null
            };

            if (val is null)
            {
                ChatGui?.PrintError("[AI Companion] Usage: /nunutls on|off");
                return;
            }

            config.AllowInsecureTls = val.Value;
            SaveConfigSafe();
            TryRebuildAiClient();

            ChatGui?.Print($"[AI Companion] AllowInsecureTls = {config.AllowInsecureTls}");
        }

        private void OnCmdNunuSave(string command, string args)
        {
            SaveConfigSafe();
            ChatGui?.Print("[AI Companion] Configuration saved.");
        }

        private void OnCmdNunuReload(string command, string args)
        {
            TryRebuildAiClient();
            ChatGui?.Print("[AI Companion] AI client rebuilt with current settings.");
        }

        private void TryRebuildAiClient()
        {
            try
            {
                ai?.Dispose();
            }
            catch { }

            try
            {
                ai = new AiClient(config, Log);
                TryLogInfo($"AI client ready. Base={config.BackendBaseUrl}, Model={config.Model}, InsecureTLS={config.AllowInsecureTls}");
            }
            catch (Exception ex)
            {
                TryLogError(ex, "Failed to initialize AI client. Check BackendBaseUrl.");
                ChatGui?.PrintError($"[AI Companion] AI init failed: {ex.Message}");
                ai = null;
            }
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
                    CommandManager.RemoveHandler("/nunuwin");
                    CommandManager.RemoveHandler("/nunucfg");
                    CommandManager.RemoveHandler("/nunuurl");
                    CommandManager.RemoveHandler("/nunumodel");
                    CommandManager.RemoveHandler("/nunutls");
                    CommandManager.RemoveHandler("/nunusave");
                    CommandManager.RemoveHandler("/nunureload");
                }
            }
            catch { }

            TryLogInfo("Disposed.");
        }
    }
}
