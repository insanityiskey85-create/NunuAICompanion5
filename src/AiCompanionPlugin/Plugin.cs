// SPDX-License-Identifier: MIT
// AiCompanionPlugin - Plugin.cs

#nullable enable
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;

namespace AiCompanionPlugin
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "AiCompanionPlugin";

        // Services (Dalamud injects these automatically)
        [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] public static IPluginLog Log { get; private set; } = null!;
        [PluginService] public static IFramework Framework { get; private set; } = null!;
        [PluginService] public static IChatGui Chat { get; private set; } = null!;

        private Configuration config = null!;
        private AiClient ai = null!;
        private PersonaManager persona = null!;

        private ChatTwoBridge? chatTwo;
        private ChatPipe? pipe;

        private readonly List<(string role, string content)> history = new();

        private ChatWindow? chatWindow;
        private SettingsWindow? settingsWindow;

        public Plugin()
        {
            // Load config
            config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            config.Initialize(PluginInterface);

            // Subsystems
            ai = new AiClient(config);
            persona = new PersonaManager(config, PluginInterface);

            // Optional ChatTwo bridge
            try { chatTwo = new ChatTwoBridge(PluginInterface); } catch { chatTwo = null; }

            pipe = new ChatPipe(config, Log, Chat, Framework, chatTwo);

            // Windows
            chatWindow = new ChatWindow(
                config,
                getHistory: () => history,
                sendUserMessage: SendUserMessage);

            settingsWindow = new SettingsWindow(config, config.Save);

            // Open on load (optional)
            if (config.OpenChatOnLoad) chatWindow.IsOpen = true;
            if (config.OpenSettingsOnLoad) settingsWindow.IsOpen = true;

            // Ui draw hooks
            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += () => settingsWindow!.IsOpen = true;
        }

        private void DrawUI()
        {
            settingsWindow?.Draw();
            chatWindow?.Draw();
        }

        private async void SendUserMessage(string userText)
        {
            // Append to in-plugin history
            history.Add(("user", userText));

            // Build system prompt if any
            var sys = persona.GetSystemPrompt();
            var msgs = new List<(string role, string content)>();
            if (!string.IsNullOrWhiteSpace(sys))
                msgs.Add(("system", sys));

            // Add recent history + this user message
            foreach (var m in history)
                msgs.Add(m);

            try
            {
                var reply = await ai.CompleteAsync(msgs).ConfigureAwait(false);
                history.Add(("assistant", reply));
            }
            catch (Exception ex)
            {
                history.Add(("assistant", $"[error] {ex.Message}"));
            }
        }

        public void Dispose()
        {
            PluginInterface.UiBuilder.Draw -= DrawUI;
            pipe?.Dispose();
            ai?.Dispose();
        }
    }
}
