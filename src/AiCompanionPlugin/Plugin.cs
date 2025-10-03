// File: src/AiCompanionPlugin/Plugin.cs
#nullable enable
using System;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin
{
    public sealed class Plugin : IDalamudPlugin, IDisposable
    {
        public string Name => "AI Companion";

        // -------- Dalamud Services (property-injected) --------
        [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static IPluginLog Log { get; private set; } = null!;
        [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
        [PluginService] internal static IFramework Framework { get; private set; } = null!;
        [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
        [PluginService] internal static Dalamud.Interface.IUiBuilder UiBuilder { get; private set; } = null!;

        // -------- Windows/System --------
        private readonly WindowSystem windowSystem = new("AI Companion");
        private SettingsWindow settingsWindow = null!;
        private ChatWindow chatWindow = null!;
        private ChronicleWindow chronicleWindow = null!;
        private MemoriesWindow memoriesWindow = null!;

        // -------- Core managers --------
        private Configuration config = null!;
        private PersonaManager persona = null!;
        private MemoryManager memory = null!;
        private ChronicleManager chronicle = null!;
        private AiClient client = null!;

        // -------- Chat & listeners --------
        private ChatPipe? chatPipe;
        private AutoRouteListener? autoRouter;
        private SayListener? sayListener;
        private PartyListener? partyListener;

        public Plugin()
        {
            // Load or create config
            config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            config.Initialize(PluginInterface);
            config.UpgradeIfNeeded();
            PluginInterface.SavePluginConfig(config);

            // Core managers (NOTE: these classes must be defined in their own files —
            // do not define them inside Plugin.cs)
            persona = new PersonaManager(PluginInterface, Log);
            memory = new MemoryManager(PluginInterface, Log, config);
            chronicle = new ChronicleManager(PluginInterface, Log, config);
            client = new AiClient(Log, config, persona, memory);

            // Chat pipe + listeners
            chatPipe = new ChatPipe(Commands, Log, Framework, ChatGui, PluginInterface, config);
            autoRouter = new AutoRouteListener(Log, ChatGui, config, chatPipe, client);
            sayListener = new SayListener(Log, ChatGui, config, chatPipe, client);
            partyListener = new PartyListener(Log, ChatGui, config, chatPipe, client);

            // Windows
            settingsWindow = new SettingsWindow(config, persona, memory, chronicle, client, chatPipe);
            chatWindow = new ChatWindow(Log, client, config, persona, memory, chronicle, chatPipe);
            chronicleWindow = new ChronicleWindow(config, chronicle);
            memoriesWindow = new MemoriesWindow(config, memory);

            windowSystem.AddWindow(settingsWindow);
            windowSystem.AddWindow(chatWindow);
            windowSystem.AddWindow(chronicleWindow);
            windowSystem.AddWindow(memoriesWindow);

            // Draw hooks
            UiBuilder.Draw += DrawUI;
            UiBuilder.OpenConfigUi += OpenConfigUi;

            // Commands
            Commands.AddHandler("/nunu", new()
            {
                HelpMessage = "Open AI Companion chat window",
                ShowInHelp = true,
                Handler = (_, _) => chatWindow.IsOpen = true
            });

            Commands.AddHandler("/nunuconfig", new()
            {
                HelpMessage = "Open AI Companion settings",
                ShowInHelp = true,
                Handler = (_, _) => settingsWindow.IsOpen = true
            });

            Log.Information("[AI Companion] Initialized.");
        }

        private void DrawUI()
        {
            windowSystem.Draw();
        }

        private void OpenConfigUi()
        {
            settingsWindow.IsOpen = true;
        }

        public void Dispose()
        {
            try
            {
                UiBuilder.Draw -= DrawUI;
                UiBuilder.OpenConfigUi -= OpenConfigUi;

                Commands.RemoveHandler("/nunu");
                Commands.RemoveHandler("/nunuconfig");

                autoRouter?.Dispose();
                sayListener?.Dispose();
                partyListener?.Dispose();
                chatPipe?.Dispose();

                windowSystem.RemoveAllWindows();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[AI Companion] Dispose exception");
            }
        }
    }

    internal class PluginServiceAttribute : Attribute
    {
    }
}
