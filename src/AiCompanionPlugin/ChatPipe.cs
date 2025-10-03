using System;
using System.Text;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

public sealed class ChatPipe
{
    private readonly ICommandManager commands;
    private readonly IPluginLog log;
    private readonly IFramework framework;
    private readonly IChatGui chatGui;
    private readonly IDalamudPluginInterface pi;
    private readonly Configuration config;

    public ChatPipe(ICommandManager commands, IPluginLog log, IFramework framework, IChatGui chatGui, IDalamudPluginInterface pi, Configuration config)
    {
        this.commands = commands;
        this.log = log;
        this.framework = framework;
        this.chatGui = chatGui;
        this.pi = pi;
        this.config = config;
    }

    public bool TrySendSay(string text)
    {
        var payload = PrepareOutbound(text, asciiOnly: config.UseAsciiOnlyNetwork);
        log.Info($"ChatPipe → ICommandManager.ProcessCommand()");
        commands.ProcessCommand($"/say {payload}");
        return true;
    }

    public bool TrySendParty(string text)
    {
        var payload = PrepareOutbound(text, asciiOnly: config.UseAsciiOnlyNetwork);
        log.Info($"ChatPipe → ICommandManager.ProcessCommand()");
        commands.ProcessCommand($"/p {payload}");
        return true;
    }

    private static string PrepareOutbound(string text, bool asciiOnly)
    {
        if (!asciiOnly) return text;
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            sb.Append(ch <= 0x7F ? ch : '?');
        }
        return sb.ToString();
    }
}
