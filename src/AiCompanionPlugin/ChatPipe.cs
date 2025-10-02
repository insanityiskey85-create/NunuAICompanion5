using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

/// <summary>
/// Outbound-only chat pipe. Routes text to PARTY chat using ICommandManager.ProcessCommand("/p ...").
/// No listeners, no subscriptions.
/// </summary>
public sealed class ChatPipe
{
    private readonly ICommandManager commands;
    private readonly IPluginLog log;
    private readonly Configuration config;

    public ChatPipe(ICommandManager commands, IPluginLog log, Configuration config)
    {
        this.commands = commands;
        this.log = log;
        this.config = config;
    }

    public async Task SendToPartyAsync(string? text, CancellationToken token = default)
    {
        if (!config.EnablePartyPipe) throw new InvalidOperationException("Party pipe is disabled in settings.");
        if (string.IsNullOrWhiteSpace(text)) return;

        text = text.Replace("\r\n", "\n").Trim();

        foreach (var chunk in Chunk(text, Math.Max(64, config.PartyChunkSize)))
        {
            token.ThrowIfCancellationRequested();
            // Force party channel via command
            var line = "/p " + chunk;
            commands.ProcessCommand(line);
            log.Info($"PartyPipe sent {chunk.Length} chars.");
            await Task.Delay(Math.Max(200, config.PartyPostDelayMs), token).ConfigureAwait(false);
        }
    }

    private static IEnumerable<string> Chunk(string s, int n)
    {
        if (s.Length <= n) { yield return s; yield break; }
        var words = s.Split(' ');
        var sb = new StringBuilder();
        foreach (var w in words)
        {
            if (sb.Length + w.Length + 1 > n)
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString().TrimEnd();
                    sb.Clear();
                }
                if (w.Length > n)
                {
                    int pos = 0;
                    while (pos < w.Length)
                    {
                        var take = Math.Min(n, w.Length - pos);
                        yield return w.Substring(pos, take);
                        pos += take;
                    }
                }
                else
                {
                    sb.Append(w).Append(' ');
                }
            }
            else
            {
                sb.Append(w).Append(' ');
            }
        }
        if (sb.Length > 0) yield return sb.ToString().TrimEnd();
    }
}
