using System;
using System.Threading;
using System.Threading.Tasks;
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

    public PartyListener PartyListener
    {
        get
        {
            return partyListener;
        }
    }

    private static Plugin? Instance;

    private readonly WindowSystem windowSystem = new("AI Companion");
    private readonly Configuration config;
    private readonly PersonaManager personaManager;
    private readonly MemoryManager memoryManager;
    private readonly ChronicleManager chronicleManager;
    private readonly AiClient aiClient;
    private readonly ChatPipe chatPipe;
    private readonly PartyListener partyListener;
    private readonly ChatWindow chatWindow;
    private readonly SettingsWindow settingsWindow;
    private readonly ChronicleWindow chronicleWindow;

    private const string CommandChat = "/aic";
    private const string CommandChron = "/aiclog";
    private const string CommandParty = "/aiparty";

    public Plugin()
    {
        Instance = this;

        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        config.Initialize(PluginInterface);

        personaManager = new PersonaManager(PluginInterface, PluginLog, config);
        memoryManager = new MemoryManager(PluginInterface, PluginLog, config);
        chronicleManager = new ChronicleManager(PluginInterface, PluginLog, config);
        aiClient = new AiClient(PluginLog, config, personaManager);
        chatPipe = new ChatPipe(CommandManager, PluginLog, config);
        partyListener = new PartyListener(ChatGui, PluginLog, config, aiClient, chatPipe);

        chatWindow = new ChatWindow(PluginLog, aiClient, config, personaManager, memoryManager, chronicleManager, chatPipe);
        settingsWindow = new SettingsWindow(config, personaManager, memoryManager, chronicleManager);
        chronicleWindow = new ChronicleWindow(config, chronicleManager);

        windowSystem.AddWindow(chatWindow);
        windowSystem.AddWindow(settingsWindow);
        windowSystem.AddWindow(chronicleWindow);

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettings;

        CommandManager.AddHandler(CommandChat, new CommandInfo(OnChat) { HelpMessage = "Open AI Companion chat window" });
        CommandManager.AddHandler(CommandChron, new CommandInfo(OnChron) { HelpMessage = "Open AI Nunu Chronicle window" });
        CommandManager.AddHandler(CommandParty, new CommandInfo(OnParty) { HelpMessage = "Send text to Party via AI Companion (/aiparty message)" });
    }

    private void OnChat(string command, string args) => chatWindow.IsOpen = true;
    private void OnChron(string command, string args) => chronicleWindow.IsOpen = true;

    private void OnParty(string command, string args)
    {
        if (!config.EnablePartyPipe)
        {
            ChatGui.PrintError("[AI Companion] Party Pipe is disabled in Settings.");
            return;
        }
        var msg = (args ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(msg))
        {
            ChatGui.PrintError("[AI Companion] Provide text: /aiparty <message>");
            return;
        }
        _ = SafePartySendAsync(msg);
    }

    private async Task SafePartySendAsync(string text)
    {
        try
        {
            using var cts = new CancellationTokenSource();
            await chatPipe.SendToPartyAsync(text, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Party pipe failed");
            ChatGui.PrintError("[AI Companion] Failed to send to party.");
        }
    }

    private void ToggleSettings() => settingsWindow.IsOpen = true;
    private void DrawUi() => windowSystem.Draw();

    public static void OpenSettingsWindow() => Instance?.settingsWindow.Open();
    public static void OpenChronicleWindow() => Instance?.chronicleWindow.Open();

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandChat);
        CommandManager.RemoveHandler(CommandChron);
        CommandManager.RemoveHandler(CommandParty);
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleSettings;
        windowSystem.RemoveAllWindows();
        PartyListener.Dispose();
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
