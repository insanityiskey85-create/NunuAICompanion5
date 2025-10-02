using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "AI Companion";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;

    private static Plugin? Instance;

    private readonly WindowSystem windowSystem = new("AI Companion");
    private readonly Configuration config;
    private readonly PersonaManager personaManager;
    private readonly MemoryManager memoryManager;
    private readonly AiClient aiClient;
    private readonly ChatWindow chatWindow;
    private readonly SettingsWindow settingsWindow;

    private const string Command = "/aic";

    public Plugin()
    {
        Instance = this;

        // Load config
        this.config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.config.Initialize(PluginInterface);

        // Services
        this.personaManager = new PersonaManager(PluginInterface, PluginLog, config);
        this.memoryManager = new MemoryManager(PluginInterface, PluginLog, config);
        this.aiClient = new AiClient(PluginLog, config, personaManager);

        // UI windows
        this.chatWindow = new ChatWindow(PluginLog, aiClient, config, personaManager, memoryManager);
        this.settingsWindow = new SettingsWindow(config, personaManager, memoryManager);

        windowSystem.AddWindow(chatWindow);
        windowSystem.AddWindow(settingsWindow);

        // Hooks
        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettings;

        // Command
        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open AI Companion chat window"
        });
    }

    private void OnCommand(string command, string args) => chatWindow.IsOpen = true;
    private void ToggleSettings() => settingsWindow.IsOpen = true;
    private void DrawUi() => windowSystem.Draw();

    // Helper so other classes can open settings without touching the UiBuilder event.
    public static void OpenSettingsWindow()
    {
        if (Instance != null)
            Instance.settingsWindow.IsOpen = true;
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(Command);
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleSettings;
        windowSystem.RemoveAllWindows();
        aiClient.Dispose();
        personaManager.Dispose();
        memoryManager.Dispose();
        Instance = null;
    }
}
