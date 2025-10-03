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

    private readonly WindowSystem windowSystem = new("AI Companion");
    private readonly Configuration config;
    private readonly PersonaManager personaManager;
    private readonly AiClient aiClient;
    private readonly ChatPipe pipe;
    private readonly ChatWindow chatWindow;
    private readonly SettingsWindow settingsWindow;
    private readonly AutoRouteListener autoRoute;
    private readonly PartyListener partyListener;
    private readonly SayListener sayListener;

    private const string Command = "/aic";

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        config.Initialize(PluginInterface);

        personaManager = new PersonaManager(PluginInterface, PluginLog, config);
        aiClient = new AiClient(PluginLog, config, personaManager);
        pipe = new ChatPipe(CommandManager, PluginLog, config, Framework, ChatGui, PluginInterface);

        chatWindow = new ChatWindow(PluginLog, aiClient, config, personaManager, pipe);
        settingsWindow = new SettingsWindow(config, personaManager);

        windowSystem.AddWindow(chatWindow);
        windowSystem.AddWindow(settingsWindow);

        autoRoute = new AutoRouteListener(PluginLog, ChatGui, config);
        partyListener = new PartyListener(PluginLog, ChatGui, config, aiClient, pipe);
        sayListener = new SayListener(PluginLog, ChatGui, config, aiClient, pipe);

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettings;

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand) { HelpMessage = "Open AI Companion chat window" });

        PluginLog.Info("[AI Companion] Initialized with channel bridge.");
    }

    private void OnCommand(string command, string args) => chatWindow.IsOpen = true;
    private void ToggleSettings() => settingsWindow.IsOpen = true;
    private void DrawUi() => windowSystem.Draw();

    public void Dispose()
    {
        CommandManager.RemoveHandler(Command);
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleSettings;

        autoRoute.Dispose();
        partyListener.Dispose();
        sayListener.Dispose();

        windowSystem.RemoveAllWindows();
        aiClient.Dispose();
        personaManager.Dispose();
        pipe.Dispose();
    }
}
