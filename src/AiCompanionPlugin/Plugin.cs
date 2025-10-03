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

    // Dalamud services
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    private static Plugin? Instance;

    // Core
    private readonly WindowSystem windowSystem = new("AI Companion");
    private readonly Configuration config;
    private readonly PersonaManager personaManager;
    private readonly MemoryManager memoryManager;
    private readonly ChronicleManager chronicleManager;
    private readonly AiClient aiClient;
    private readonly ChatPipe chatPipe;
    private readonly AutoRouteListener autoListener;

    // Windows
    private readonly ChatWindow chatWindow;
    private readonly SettingsWindow settingsWindow;
    private readonly ChronicleWindow chronicleWindow;

    // Commands
    private const string CommandChat = "/aic";
    private const string CommandChron = "/aiclog";

    public Plugin()
    {
        Instance = this;

        // Load / init config
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        config.Initialize(PluginInterface);

        // Managers & services
        personaManager = new PersonaManager(PluginInterface, PluginLog, config);
        memoryManager = new MemoryManager(PluginInterface, PluginLog, config);
        chronicleManager = new ChronicleManager(PluginInterface, PluginLog, config);
        aiClient = new AiClient(PluginLog, config, personaManager);
        chatPipe = new ChatPipe(CommandManager, PluginLog, config, Framework, ChatGui);
        autoListener = new AutoRouteListener(ChatGui, PluginLog, config, aiClient, chatPipe);

        // UI
        chatWindow = new ChatWindow(PluginLog, aiClient, config, personaManager, memoryManager, chronicleManager, chatPipe);
        settingsWindow = new SettingsWindow(config, personaManager, memoryManager, chronicleManager);
        chronicleWindow = new ChronicleWindow(config, chronicleManager);

        windowSystem.AddWindow(chatWindow);
        windowSystem.AddWindow(settingsWindow);
        windowSystem.AddWindow(chronicleWindow);

        // Hooks
        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettings;

        // Commands
        CommandManager.AddHandler(CommandChat, new CommandInfo(OnChat) { HelpMessage = "Open AI Companion chat window" });
        CommandManager.AddHandler(CommandChron, new CommandInfo(OnChron) { HelpMessage = "Open AI Companion chronicle window" });

        PluginLog.Info("[AI Companion] Initialized.");
    }

    private void OnChat(string command, string args)
    {
        PluginLog.Info("[AI Companion] /aic invoked.");
        chatWindow.IsOpen = true;
    }

    private void OnChron(string command, string args)
    {
        PluginLog.Info("[AI Companion] /aiclog invoked.");
        chronicleWindow.IsOpen = true;
    }

    private void ToggleSettings()
    {
        PluginLog.Info("[AI Companion] OpenConfigUi invoked.");
        settingsWindow.IsOpen = true;
    }

    private void DrawUi()
    {
        try
        {
            windowSystem.Draw();
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "WindowSystem.Draw failed");
        }
    }

    // Convenience for other classes
    public static void OpenSettingsWindow() => Instance?.settingsWindow.Open();
    public static void OpenChatWindow() => Instance?.chatWindow.Open();

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandChat);
        CommandManager.RemoveHandler(CommandChron);

        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleSettings;

        windowSystem.RemoveAllWindows();

        autoListener.Dispose();
        chatPipe.Dispose();
        aiClient.Dispose();
        personaManager.Dispose();
        memoryManager.Dispose();
        chronicleManager.Dispose();

        Instance = null;
    }
}

// tiny helper to open any Window
file static class WindowExt
{
    public static void Open(this Window w) => w.IsOpen = true;
}
