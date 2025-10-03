using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Text;

namespace AiCompanionPlugin;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "AI Companion";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    private static readonly WindowSystem WindowSystem = new("AI Companion");

    private static SettingsWindow? settingsWindow;
    private static ChatWindow? chatWindow;

    private readonly Configuration config;
    private readonly PersonaManager personaManager;
    private readonly AiClient aiClient;
    private readonly ChatPipe pipe;
    private readonly AutoRouteListener autoRoute;
    private readonly SayListener sayListener;
    private readonly PartyListener partyListener;

    private const string Command = "/aic";

    public Plugin()
    {
        // Load config
        this.config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.config.Initialize(PluginInterface);

        // Persona manager
        this.personaManager = new PersonaManager(PluginInterface, PluginLog, config);

        // AI client
        this.aiClient = new AiClient(PluginLog, config, personaManager);

        // Outbound chat pipe (rate-limited queue)
        this.pipe = new ChatPipe(PluginInterface, CommandManager, Framework, ChatGui, PluginLog);

        // Windows
        chatWindow = new ChatWindow(PluginLog, aiClient, config, personaManager, pipe);
        settingsWindow = new SettingsWindow(config, personaManager);

        WindowSystem.AddWindow(chatWindow);
        WindowSystem.AddWindow(settingsWindow);

        // UI hooks
        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettings;

        // Listeners
        this.autoRoute = new AutoRouteListener(PluginLog, ChatGui, config);
        this.sayListener = new SayListener(PluginLog, ChatGui, config);
        this.partyListener = new PartyListener(PluginLog, ChatGui, config);

        // Command
        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open AI Companion chat window"
        });

        PluginLog.Information("[AI Companion] Initialized with channel bridge.");
    }

    private void OnCommand(string command, string args) => OpenChatWindow();

    private static void OpenChatWindow()
    {
        if (chatWindow != null) chatWindow.IsOpen = true;
    }

    private static void ToggleSettings() => OpenSettingsWindow();

    public static void OpenSettingsWindow()
    {
        if (settingsWindow != null) settingsWindow.IsOpen = true;
    }

    private void DrawUi() => WindowSystem.Draw();

    public void Dispose()
    {
        CommandManager.RemoveHandler(Command);
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleSettings;
        WindowSystem.RemoveAllWindows();

        autoRoute.Dispose();
        sayListener.Dispose();
        partyListener.Dispose();
        pipe.Dispose();
        aiClient.Dispose();
        personaManager.Dispose();
    }

    /// <summary>
    /// Called by AutoRouteListener when a trigger is detected in Party/Say.
    /// Surfaces the message into the AI Companion window (and can kick off replies).
    /// </summary>
    public static void RouteIncoming(XivChatType type, string sender, string text)
    {
        chatWindow?.OnRoutedMessage(type, sender, text);
    }
}
