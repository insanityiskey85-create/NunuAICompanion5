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

    private readonly WindowSystem windowSystem = new("AI Companion");
    private readonly Configuration config;
    private readonly PersonaManager personaManager;
    private readonly AiClient aiClient;
    private readonly ChatWindow chatWindow;
    private readonly SettingsWindow settingsWindow;

    private const string Command = "/aic";

    public Plugin()
    {
        // Load config
        this.config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.config.Initialize(PluginInterface);

        // Persona manager (reads persona.txt and watches for changes)
        this.personaManager = new PersonaManager(PluginInterface, PluginLog, config);

        // Minimal AI HTTP client
        this.aiClient = new AiClient(PluginLog, config, personaManager);

        // UI windows
        this.chatWindow = new ChatWindow(PluginLog, aiClient, config, personaManager);
        this.settingsWindow = new SettingsWindow(config, personaManager);

        windowSystem.AddWindow(chatWindow);
        windowSystem.AddWindow(settingsWindow);

        // Hook UI draw
        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettings;

        // Command: open the chat window
        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open AI Companion chat window"
        });
    }

    private void OnCommand(string command, string args)
    {
        chatWindow.IsOpen = true;
    }

    private void ToggleSettings()
    {
        settingsWindow.IsOpen = true;
    }

    private void DrawUi() => windowSystem.Draw();

    public void Dispose()
    {
        CommandManager.RemoveHandler(Command);
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleSettings;
        windowSystem.RemoveAllWindows();
        aiClient.Dispose();
        personaManager.Dispose();
    }
}
