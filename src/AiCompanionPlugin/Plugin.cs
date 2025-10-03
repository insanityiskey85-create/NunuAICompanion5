// SPDX-License-Identifier: MIT
// AiCompanionPlugin - Plugin.cs
//
// Wires UiBuilder so SettingsWindow and ChatWindow draw each frame.
// Adds slash commands:
//   /nunuwin  -> toggle Chat window
//   /nunucfg  -> open Settings window
//
// Keeps a tiny in-memory chat history so the Chat window has content
// even before your backend is online.

#nullable enable
using System;
using System.Collections.Generic;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Command;

namespace AiCompanionPlugin
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "AI Companion";

        // ---- Dalamud services (populated by property injection after construction) ----
        [PluginService] internal IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal IPluginLog? Log { get; private set; }
        [PluginService] internal IFramework? Framework { get; private set; }
        [PluginService] internal IChatGui? ChatGui { get; private set; }
        [PluginService] internal ICommandManager? CommandManager { get; private set; }

        // ---- Windows ----
        private readonly SettingsWindow settingsWindow;
        private readonly ChatWindow chatWindow;

        // ---- State ----
        private readonly Configuration config;
        private readonly List<(string role, string content)> history = new();

        // command registration guard
        private bool commandsRegistered = false;

        public Plugin(IDalamudPluginInterface pi)
        {
            // Load or create config synchronously; safe inside ctor.
            var cfg = pi.GetPluginConfig() as Configuration ?? new Configuration();
            cfg.Initialize(pi);
            config = cfg;

            // Seed a friendly line so ChatWindow isn't empty on first open.
            history.Add(("system", "Nunu hums a testing note. WAH!"));

            // Create windows immediately; they don't require injected services.
            settingsWindow = new SettingsWindow(config, SaveConfigSafe);
            chatWindow = new ChatWindow(config, GetHistory, SendUserMessage);

            // Hook UI using the interface we already have.
            pi.UiBuilder.Draw += DrawUI;
            pi.UiBuilder.OpenConfigUi += OpenConfigUi;

            TryOpenOnLoad();
            TryLogInfo("AI Companion constructed. UI wired; commands will register on first draw.");
        }

        // ---------------- UI plumbing ----------------

        private void DrawUI()
        {
            // Make sure slash commands are registered once we have CommandManager injected.
            EnsureCommands();

            settingsWindow.Draw();
            chatWindow.Draw();
        }

        private void OpenConfigUi()
        {
            settingsWindow.IsOpen = true;
        }

        private void TryOpenOnLoad()
        {
            try
            {
                if (config.GetType().GetProperty("OpenSettingsOnLoad")?.GetValue(config) is bool openSettings && openSettings)
                    settingsWindow.IsOpen = true;

                if (config.GetType().GetProperty("OpenChatOnLoad")?.GetValue(config) is bool openChat && openChat)
                    chatWindow.IsOpen = true;
            }
            catch
            {
                // Ignore if fields aren't present
            }
        }

        // ---------------- Slash commands ----------------

        private void EnsureCommands()
        {
            if (commandsRegistered) return;
            if (CommandManager is null) return; // not injected yet; will try again next frame

            try
            {
                CommandManager.AddHandler("/nunuwin", new CommandInfo(OnCmdNunuWin)
                {
                    HelpMessage = "Toggle AI Companion chat window."
                });

                CommandManager.AddHandler("/nunucfg", new CommandInfo(OnCmdNunuCfg)
                {
                    HelpMessage = "Open AI Companion settings window."
                });

                commandsRegistered = true;
                TryLogInfo("Slash commands registered: /nunuwin, /nunucfg");
            }
            catch (Exception ex)
            {
                TryLogError(ex, "Failed to register slash commands");
            }
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

        // ---------------- Chat wiring for the ChatWindow ----------------

        private IReadOnlyList<(string role, string content)> GetHistory()
            => history;

        private void SendUserMessage(string message)
        {
            history.Add(("user", message));
            try { ChatGui?.Print($"[AI Companion] You: {message}"); } catch { /* ignore */ }

            // Placeholder reply so UI proves it's alive.
            var reply = $"♪ Nunu heard: {message}";
            history.Add(("assistant", reply));
        }

        // ---------------- Util ----------------

        private void SaveConfigSafe()
        {
            try
            {
                config.Save();
                TryLogInfo("Configuration saved.");
            }
            catch (Exception ex)
            {
                TryLogError(ex, "Failed to save configuration");
            }
        }

        private void TryLogInfo(string msg)
        {
            try { Log?.Info(msg); } catch { /* ignore */ }
        }

        private void TryLogError(Exception ex, string msg)
        {
            try { Log?.Error(ex, msg); } catch { /* ignore */ }
        }

        public void Dispose()
        {
            try
            {
                // Unhook UI
                PluginInterface?.UiBuilder.Draw -= DrawUI;
                PluginInterface?.UiBuilder.OpenConfigUi -= OpenConfigUi;

                // Unregister commands if they were registered
                if (commandsRegistered && CommandManager is not null)
                {
                    CommandManager.RemoveHandler("/nunuwin");
                    CommandManager.RemoveHandler("/nunucfg");
                }
            }
            catch { /* ignore */ }

            TryLogInfo("AI Companion disposed.");
        }
    }
}
