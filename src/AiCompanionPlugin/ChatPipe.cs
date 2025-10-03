// File: ChatPipe.cs
using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;

namespace AiCompanionPlugin;

/// <summary>
/// Queued, rate-limited outbound chat. Can post to /say and /party by simulating
/// user commands through ICommandManager.ProcessCommand(). Falls back gracefully
/// if a bridge is unavailable.
/// </summary>
public sealed class ChatPipe : IDisposable
{
    private readonly IDalamudPluginInterface pi;
    private readonly ICommandManager commands;
    private readonly IPluginLog log;
    private readonly IFramework framework;
    private readonly IChatGui chatGui;
    private readonly ChatTwoBridge chatTwo;

    private readonly Queue<(XivChatType channel, string text)> queue = new();
    private DateTime lastSend = DateTime.MinValue;
    private bool queuedUpdate;

    public ChatPipe(
        IDalamudPluginInterface pi,
        ICommandManager commands,
        IFramework framework,
        IChatGui chatGui,
        IPluginLog log)
    {
        this.pi = pi;
        this.commands = commands;
        this.framework = framework;
        this.chatGui = chatGui;
        this.log = log;
        this.chatTwo = new ChatTwoBridge(pi, log);

        this.framework.Update += OnUpdate;
    }

    public ChatPipe(ICommandManager commandManager, IPluginLog pluginLog, Configuration config, IFramework framework, IChatGui chatGui, IDalamudPluginInterface pluginInterface)
    {
        this.framework = framework;
        this.chatGui = chatGui;
    }

    public void Dispose()
    {
        this.framework.Update -= OnUpdate;
    }

    /// <summary>Add an outbound message to the queue.</summary>
    public void EnqueueSay(string text) => Enqueue(XivChatType.Say, text);
    public void EnqueueParty(string text) => Enqueue(XivChatType.Party, text);

    private void Enqueue(XivChatType channel, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        queue.Enqueue((channel, Sanitize(text)));
        if (!queuedUpdate)
        {
            queuedUpdate = true;
            // immediate tick to avoid long delays
            OnUpdate(null!);
        }
    }

    private static string Sanitize(string s)
    {
        // Avoid control characters that can be eaten by chat.
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (!char.IsControl(ch) || ch == '\n' || ch == '\r' || ch == '\t')
                sb.Append(ch);
        }
        return sb.ToString();
    }

    private void OnUpdate(IFramework _)
    {
        var now = DateTime.UtcNow;
        // throttle to ~8 messages / sec max
        var minInterval = TimeSpan.FromMilliseconds(120);
        if (now - lastSend < minInterval) return;

        if (queue.Count == 0) { queuedUpdate = false; return; }

        var (channel, text) = queue.Dequeue();
        lastSend = now;

        bool ok = TrySend(channel, text);
        log.Information($"ChatPipe → ICommandManager.ProcessCommand()");
        log.Information($"ChatPipe({channel}) sent {text.Length} chars.");
    }

    private bool TrySend(XivChatType channel, string text)
    {
        // Prefer external bridge if available (stub returns false)
        if (chatTwo.IsAvailable)
        {
            if (channel == XivChatType.Say && chatTwo.TrySendSay(text)) return true;
            if (channel == XivChatType.Party && chatTwo.TrySendParty(text)) return true;
        }

        // Fallback: use command manager to dispatch typed chat commands.
        return channel switch
        {
            XivChatType.Say => TryCommandDispatch($"/say {text}"),
            XivChatType.Party => TryCommandDispatch($"/p {text}"),
            _ => TryCommandDispatch($"/say {text}")
        };
    }

    /// <summary>
    /// Low-level: dispatch a chat command exactly as the user would type it.
    /// </summary>
    private bool TryCommandDispatch(string commandLine)
    {
        try
        {
            // NOTE: this is the supported way for plugins to fire commands.
            commands.ProcessCommand(commandLine);
            return true;
        }
        catch (Exception ex)
        {
            log.Error(ex, "ProcessCommand failed for: {Command}", commandLine);
            return false;
        }
    }
}
