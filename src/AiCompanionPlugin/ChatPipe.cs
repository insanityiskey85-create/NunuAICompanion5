using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

public enum ChatRoute { Party, Say }

/// <summary>
/// Outbound router to /p or /s using ICommandManager.ProcessCommand.
/// Supports normal and streaming sends.
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

    private (string prefixCmd, int chunk, int delay, int flushChars, int flushMs, bool enabled, string aiPrefix) GetRouteParams(ChatRoute route)
    {
        var aiPrefix = string.IsNullOrWhiteSpace(config.AiDisplayName) ? "[AI Nunu] " : $"[{config.AiDisplayName}] ";
        return route switch
        {
            ChatRoute.Party => ("/p ", Math.Max(64, config.PartyChunkSize), Math.Max(200, config.PartyPostDelayMs),
                                Math.Max(40, config.PartyStreamFlushChars), Math.Max(300, config.PartyStreamMinFlushMs),
                                config.EnablePartyPipe, aiPrefix),
            ChatRoute.Say => ("/s ", Math.Max(64, config.SayChunkSize), Math.Max(200, config.SayPostDelayMs),
                                Math.Max(40, config.SayStreamFlushChars), Math.Max(300, config.SayStreamMinFlushMs),
                                config.EnableSayPipe, aiPrefix),
            _ => ("/p ", 440, 800, 180, 600, false, aiPrefix),
        };
    }

    /// <summary>Send a whole message to the given route (Party/Say), chunked and delayed.</summary>
    public async Task SendToAsync(ChatRoute route, string? text, CancellationToken token = default, bool addPrefix = true)
    {
        var (cmd, chunkSize, delay, _, _, enabled, aiPrefix) = GetRouteParams(route);
        if (!enabled) throw new InvalidOperationException($"{route} pipe is disabled in settings.");
        if (string.IsNullOrWhiteSpace(text)) return;

        var prefix = addPrefix ? aiPrefix : string.Empty;
        text = text.Replace("\r\n", "\n").Trim();

        var chunks = ChunkSmart(text, chunkSize - prefix.Length);
        bool first = true;

        foreach (var raw in chunks)
        {
            token.ThrowIfCancellationRequested();
            var line = first ? prefix + raw : "…" + raw;
            first = false;

            commands.ProcessCommand(cmd + line);
            log.Info($"ChatPipe({route}) sent {line.Length} chars.");
            await Task.Delay(delay, token).ConfigureAwait(false);
        }
    }

    /// <summary>Stream tokens to route with header on first line and continuation thereafter.</summary>
    public async Task SendStreamingToAsync(ChatRoute route, IAsyncEnumerable<string> tokens, string headerForFirstLine, CancellationToken token = default)
    {
        var (cmd, chunkSize, delay, flushChars, flushMs, enabled, aiPrefix) = GetRouteParams(route);
        if (!enabled) throw new InvalidOperationException($"{route} pipe is disabled in settings.");

        headerForFirstLine ??= string.Empty;
        var firstPrefix = aiPrefix + headerForFirstLine;
        var contPrefix = "…";

        var firstMax = Math.Max(64, chunkSize - firstPrefix.Length);
        var contMax = Math.Max(64, chunkSize - contPrefix.Length);

        var sb = new StringBuilder();
        var first = true;
        var lastFlush = Environment.TickCount;

        await foreach (var t in tokens.WithCancellation(token))
        {
            if (string.IsNullOrEmpty(t)) continue;
            sb.Append(t);

            var elapsed = Environment.TickCount - lastFlush;
            var needFlush = sb.Length >= flushChars || elapsed >= flushMs;

            while (needFlush && sb.Length > 0)
            {
                token.ThrowIfCancellationRequested();

                if (first)
                {
                    var take = Math.Min(firstMax, sb.Length);
                    var chunk = sb.ToString(0, take);
                    sb.Remove(0, take);
                    commands.ProcessCommand(cmd + firstPrefix + chunk);
                    first = false;
                }
                else
                {
                    var take = Math.Min(contMax, sb.Length);
                    var chunk = sb.ToString(0, take);
                    sb.Remove(0, take);
                    commands.ProcessCommand(cmd + contPrefix + chunk);
                }

                log.Info($"ChatPipe({route}) streamed chunk.");
                lastFlush = Environment.TickCount;
                await Task.Delay(delay, token).ConfigureAwait(false);

                needFlush = sb.Length >= flushChars || (Environment.TickCount - lastFlush) >= flushMs;
            }
        }

        // Final flush
        while (sb.Length > 0)
        {
            token.ThrowIfCancellationRequested();
            var take = Math.Min(first ? firstMax : contMax, sb.Length);
            var chunk = sb.ToString(0, take);
            sb.Remove(0, take);
            commands.ProcessCommand(cmd + (first ? firstPrefix : contPrefix) + chunk);
            first = false;
            await Task.Delay(delay, token).ConfigureAwait(false);
        }
    }

    // ---------- helpers ----------
    private static IEnumerable<string> ChunkSmart(string text, int max)
    {
        if (text.Length <= max) { yield return text; yield break; }

        var sentences = SplitSentences(text);
        var sb = new StringBuilder();

        foreach (var s in sentences)
        {
            if (s.Length > max)
            {
                foreach (var chunk in ChunkWords(s, max))
                    yield return chunk;
            }
            else
            {
                if (sb.Length + s.Length + 1 <= max)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(s);
                }
                else
                {
                    if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                    sb.Append(s);
                }
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    private static IEnumerable<string> ChunkWords(string s, int max)
    {
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var w in words)
        {
            if (w.Length > max)
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                int p = 0;
                while (p < w.Length)
                {
                    int take = Math.Min(max, w.Length - p);
                    yield return w.Substring(p, take);
                    p += take;
                }
            }
            else if (sb.Length + w.Length + 1 > max)
            {
                yield return sb.ToString();
                sb.Clear();
                sb.Append(w);
            }
            else
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(w);
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    private static IEnumerable<string> SplitSentences(string text)
    {
        var t = text.Replace("\r\n", "\n");
        var parts = t.Split(new[] { ". ", "! ", "? ", "\n" }, StringSplitOptions.None);
        for (int i = 0; i < parts.Length; i++)
        {
            var seg = parts[i].Trim();
            if (seg.Length == 0) continue;
            if (i < parts.Length - 1 && !t.Contains('\n'))
                seg += ".";
            yield return seg;
        }
    }
}
