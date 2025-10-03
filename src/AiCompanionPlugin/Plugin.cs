using System;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
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
    private readonly ChatWindow chatWindow;
    private readonly SettingsWindow settingsWindow;

    private const string Command = "/aic";

    public Plugin()
    {
        // Load config
        this.config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.config.Initialize(PluginInterface);

        // Persona / AI
        this.personaManager = new PersonaManager(PluginInterface, PluginLog, config);
        this.aiClient = new AiClient(PluginLog, config, personaManager);

        // Settings window
        this.settingsWindow = new SettingsWindow(config, personaManager);

        // Chat window (pass a callback that opens settings)
        this.chatWindow = new ChatWindow(
            PluginLog,
            aiClient,
            config,
            personaManager,
            openSettings: () => this.settingsWindow.Show()
        );

        windowSystem.AddWindow(chatWindow);
        windowSystem.AddWindow(settingsWindow);

        // UI integration
        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += OpenSettingsWindow;

        // Command
        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open AI Companion chat window"
        });
    }

    private void OnCommand(string command, string args)
    {
        chatWindow.Show();
    }

    private void OpenSettingsWindow()
    {
        settingsWindow.Show();
    }

    private void DrawUi() => windowSystem.Draw();

    public void Dispose()
    {
        CommandManager.RemoveHandler(Command);
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenSettingsWindow;

        windowSystem.RemoveAllWindows();

        aiClient.Dispose();
        personaManager.Dispose();
    }

    internal static void RouteIncoming(XivChatType type, string name, string v) => throw new NotImplementedException();
}
