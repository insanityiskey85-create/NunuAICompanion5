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
    private readonly ChronicleManager chronicleManager;
    private readonly AiClient aiClient;
    private readonly ChatWindow chatWindow;
    private readonly SettingsWindow settingsWindow;
    private readonly ChronicleWindow chronicleWindow;

    private const string CommandChat = "/aic";
    private const string CommandChron = "/aiclog";

    public Plugin()
    {
        Instance = this;

        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        config.Initialize(PluginInterface);

        personaManager = new PersonaManager(PluginInterface, PluginLog, config);
        memoryManager = new MemoryManager(PluginInterface, PluginLog, config);
        chronicleManager = new ChronicleManager(PluginInterface, PluginLog, config);
        aiClient = new AiClient(PluginLog, config, personaManager);

        chatWindow = new ChatWindow(PluginLog, aiClient, config, personaManager, memoryManager, chronicleManager);
        settingsWindow = new SettingsWindow(config, personaManager, memoryManager, chronicleManager);
        chronicleWindow = new ChronicleWindow(config, chronicleManager);

        windowSystem.AddWindow(chatWindow);
        windowSystem.AddWindow(settingsWindow);
        windowSystem.AddWindow(chronicleWindow);

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettings;

        CommandManager.AddHandler(CommandChat, new CommandInfo(OnChat) { HelpMessage = "Open AI Companion chat window" });
        CommandManager.AddHandler(CommandChron, new CommandInfo(OnChron) { HelpMessage = "Open AI Nunu Chronicle window" });
    }

    private void OnChat(string command, string args) => chatWindow.IsOpen = true;
    private void OnChron(string command, string args) => chronicleWindow.IsOpen = true;
    private void ToggleSettings() => settingsWindow.IsOpen = true;
    private void DrawUi() => windowSystem.Draw();

    public static void OpenSettingsWindow() => Instance?.settingsWindow.Open();
    public static void OpenChronicleWindow() => Instance?.chronicleWindow.Open();

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandChat);
        CommandManager.RemoveHandler(CommandChron);
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleSettings;
        windowSystem.RemoveAllWindows();
        aiClient.Dispose();
        personaManager.Dispose();
        memoryManager.Dispose();
        chronicleManager.Dispose();
        Instance = null;
    }
}

file static class WindowExt
{
    public static void Open(this Window w) => w.IsOpen = true;
}
