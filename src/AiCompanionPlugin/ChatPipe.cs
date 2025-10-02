﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

/// <summary>
/// Outbound-only router to PARTY chat. Uses ICommandManager.ProcessCommand("/p ...").
/// Supports normal sends and streaming sends.
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

    /// <summary>Send text to PARTY, chunked and delayed. addPrefix=true adds "[AI Nunu] ".</summary>
    public async Task SendToPartyAsync(string? text, CancellationToken token = default, bool addPrefix = true)
    {
        if (!config.EnablePartyPipe) throw new InvalidOperationException("Party pipe is disabled in settings.");
        if (string.IsNullOrWhiteSpace(text)) return;

        var prefix = addPrefix
            ? (string.IsNullOrWhiteSpace(config.AiDisplayName) ? "[AI Nunu] " : $"[{config.AiDisplayName}] ")
            : string.Empty;

        text = text.Replace("\r\n", "\n").Trim();

        var chunks = ChunkSmart(text, Math.Max(64, config.PartyChunkSize - prefix.Length));
        bool first = true;

        foreach (var raw in chunks)
        {
            token.ThrowIfCancellationRequested();
            var line = first ? prefix + raw : "…" + raw;
            first = false;

            commands.ProcessCommand("/p " + line);
            log.Info($"PartyPipe sent {line.Length} chars.");
            await Task.Delay(Math.Max(200, config.PartyPostDelayMs), token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stream tokens to PARTY as multiple lines.
    /// The first line includes a custom header (e.g., "AI Nunu → Caller: "), subsequent lines start with "…".
    /// </summary>
    public async Task SendStreamingToPartyAsync(
        IAsyncEnumerable<string> tokens,
        string headerForFirstLine,
        CancellationToken token = default)
    {
        if (!config.EnablePartyPipe) throw new InvalidOperationException("Party pipe is disabled in settings.");

        headerForFirstLine ??= string.Empty;
        var firstPrefix = (string.IsNullOrWhiteSpace(config.AiDisplayName) ? "[AI Nunu] " : $"[{config.AiDisplayName}] ")
                          + headerForFirstLine;
        var contPrefix = "…";

        var firstMax = Math.Max(64, config.PartyChunkSize - firstPrefix.Length);
        var contMax = Math.Max(64, config.PartyChunkSize - contPrefix.Length);

        var sb = new StringBuilder();
        var first = true;
        var lastFlush = Environment.TickCount;

        await foreach (var t in tokens.WithCancellation(token))
        {
            if (string.IsNullOrEmpty(t)) continue;
            sb.Append(t);

            var elapsed = Environment.TickCount - lastFlush;
            var flushChars = Math.Max(40, config.PartyStreamFlushChars);
            var needFlush = sb.Length >= flushChars || elapsed >= Math.Max(300, config.PartyStreamMinFlushMs);

            while (needFlush && sb.Length > 0)
            {
                token.ThrowIfCancellationRequested();

                if (first)
                {
                    var take = Math.Min(firstMax, sb.Length);
                    var chunk = sb.ToString(0, take);
                    sb.Remove(0, take);

                    commands.ProcessCommand("/p " + firstPrefix + chunk);
                    first = false;
                }
                else
                {
                    var take = Math.Min(contMax, sb.Length);
                    var chunk = sb.ToString(0, take);
                    sb.Remove(0, take);

                    commands.ProcessCommand("/p " + contPrefix + chunk);
                }

                log.Info("PartyPipe streamed chunk.");
                lastFlush = Environment.TickCount;
                await Task.Delay(Math.Max(200, config.PartyPostDelayMs), token).ConfigureAwait(false);

                // If still long, keep looping; else break
                needFlush = sb.Length >= flushChars || (Environment.TickCount - lastFlush) >= Math.Max(300, config.PartyStreamMinFlushMs);
            }
        }

        // Final flush
        if (sb.Length > 0)
        {
            token.ThrowIfCancellationRequested();

            if (first)
            {
                var take = Math.Min(firstMax, sb.Length);
                var chunk = sb.ToString(0, take);
                sb.Remove(0, take);
                commands.ProcessCommand("/p " + firstPrefix + chunk);
            }
            else
            {
                while (sb.Length > 0)
                {
                    var take = Math.Min(contMax, sb.Length);
                    var chunk = sb.ToString(0, take);
                    sb.Remove(0, take);
                    commands.ProcessCommand("/p " + contPrefix + chunk);
                    await Task.Delay(Math.Max(200, config.PartyPostDelayMs), token).ConfigureAwait(false);
                }
            }
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
