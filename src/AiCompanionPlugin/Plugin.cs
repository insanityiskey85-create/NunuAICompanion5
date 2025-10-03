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
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    private static Plugin? Instance;

    private readonly WindowSystem windowSystem = new("AI Companion");
    private readonly Configuration config;
    private readonly PersonaManager personaManager;
    private readonly MemoryManager memoryManager;
    private readonly ChronicleManager chronicleManager;
    private readonly AiClient aiClient;
    private readonly ChatPipe chatPipe;
    private readonly AutoRouteListener autoListener;

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

        chatWindow = new ChatWindow(PluginLog, aiClient, config, personaManager, memoryManager, chronicleManager, pipe: null! /* temp */);
        chatPipe = new ChatPipe(CommandManager, PluginLog, config, Framework, ChatGui);
        // re-create window with a valid pipe reference
        chatWindow = new ChatWindow(PluginLog, aiClient, config, personaManager, memoryManager, chronicleManager, chatPipe);

        settingsWindow = new SettingsWindow(config, personaManager, memoryManager, chronicleManager);
        chronicleWindow = new ChronicleWindow(config, chronicleManager);

        windowSystem.AddWindow(chatWindow);
        windowSystem.AddWindow(settingsWindow);
        windowSystem.AddWindow(chronicleWindow);

        // Listener now pushes into the chat window and prepares replies for manual posting
        autoListener = new AutoRouteListener(ChatGui, PluginLog, config, aiClient, chatPipe, chatWindow);

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettings;

        CommandManager.AddHandler(CommandChat, new CommandInfo(OnChat) { HelpMessage = "Open AI Companion chat window" });
        CommandManager.AddHandler(CommandChron, new CommandInfo(OnChron) { HelpMessage = "Open AI Companion chronicle window" });

        PluginLog.Info("[AI Companion] Initialized with channel bridge.");
    }

    private void OnChat(string command, string args) => chatWindow.IsOpen = true;
    private void OnChron(string command, string args) => chronicleWindow.IsOpen = true;

    private void ToggleSettings() => settingsWindow.IsOpen = true;

    private void DrawUi()
    {
        try { windowSystem.Draw(); }
        catch (Exception ex) { PluginLog.Error(ex, "WindowSystem.Draw failed"); }
    }

    public static void OpenSettingsWindow() => Instance?.settingsWindow.Open();

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

file static class WindowExt
{
    public static void Open(this Window w) => w.IsOpen = true;
}
