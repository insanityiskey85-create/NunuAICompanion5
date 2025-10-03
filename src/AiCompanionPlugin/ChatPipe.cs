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
/// Outbound router to /party or /say using ICommandManager.ProcessCommand.
/// All sends are marshalled to the framework (game) thread.
/// Supports normal and streaming sends. Safe no-throw if route is disabled.
/// </summary>
public sealed class ChatPipe
{
    private readonly ICommandManager commands;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly IFramework framework;
    private readonly IChatGui chat; // for optional debug echo

    public ChatPipe(ICommandManager commands, IPluginLog log, Configuration config, IFramework framework, IChatGui chat)
    {
        this.commands = commands;
        this.log = log;
        this.config = config;
        this.framework = framework;
        this.chat = chat;
    }

    private (string cmdPrefix, int chunk, int delay, int flushChars, int flushMs, bool enabled, string aiPrefix) GetRouteParams(ChatRoute route)
    {
        var aiPrefix = string.IsNullOrWhiteSpace(config.AiDisplayName) ? "[AI Nunu] " : $"[{config.AiDisplayName}] ";
        return route switch
        {
            ChatRoute.Party => ("/party ", Math.Max(64, config.PartyChunkSize), Math.Max(200, config.PartyPostDelayMs),
                                Math.Max(40, config.PartyStreamFlushChars), Math.Max(300, config.PartyStreamMinFlushMs),
                                config.EnablePartyPipe, aiPrefix),
            ChatRoute.Say => ("/say ", Math.Max(64, config.SayChunkSize), Math.Max(200, config.SayPostDelayMs),
                                Math.Max(40, config.SayStreamFlushChars), Math.Max(300, config.SayStreamMinFlushMs),
                                config.EnableSayPipe, aiPrefix),
            _ => ("/say ", 440, 800, 180, 600, false, aiPrefix),
        };
    }

    /// <summary>
    /// Send a whole message to the given route (Party/Say), chunked and delayed.
    /// Returns false if the route is disabled; true if sent.
    /// </summary>
    public async Task<bool> SendToAsync(ChatRoute route, string? text, CancellationToken token = default, bool addPrefix = true)
    {
        var (cmd, chunkSize, delay, _, _, enabled, aiPrefix) = GetRouteParams(route);
        if (!enabled) { log.Info($"ChatPipe({route}): pipe disabled, skipping send."); return false; }
        if (string.IsNullOrWhiteSpace(text)) return false;

        var prefix = addPrefix ? aiPrefix : string.Empty;
        text = text.Replace("\r\n", "\n").Trim();

        var chunks = ChunkSmart(text, chunkSize - prefix.Length);
        bool first = true;

        foreach (var raw in chunks)
        {
            token.ThrowIfCancellationRequested();
            var line = first ? prefix + raw : "…" + raw;
            first = false;

            await RunOnFrameworkAsync(() => commands.ProcessCommand(cmd + line), token).ConfigureAwait(false);
            if (config.DebugChatTap) chat.Print($"[AI Companion:{route}] → {line}");
            log.Info($"ChatPipe({route}) sent {line.Length} chars.");
            await Task.Delay(delay, token).ConfigureAwait(false);
        }
        return true;
    }

    /// <summary>
    /// Stream tokens to route with header on first line and continuation thereafter.
    /// Returns false if the route is disabled; true if streamed.
    /// </summary>
    public async Task<bool> SendStreamingToAsync(ChatRoute route, IAsyncEnumerable<string> tokens, string headerForFirstLine, CancellationToken token = default)
    {
        var (cmd, chunkSize, delay, flushChars, flushMs, enabled, aiPrefix) = GetRouteParams(route);
        if (!enabled) { log.Info($"ChatPipe({route}): pipe disabled, skipping stream."); return false; }

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

                    await RunOnFrameworkAsync(() => commands.ProcessCommand(cmd + firstPrefix + chunk), token).ConfigureAwait(false);
                    if (config.DebugChatTap) chat.Print($"[AI Companion:{route}] → {firstPrefix}{chunk}");
                    first = false;
                }
                else
                {
                    var take = Math.Min(contMax, sb.Length);
                    var chunk = sb.ToString(0, take);
                    sb.Remove(0, take);

                    await RunOnFrameworkAsync(() => commands.ProcessCommand(cmd + contPrefix + chunk), token).ConfigureAwait(false);
                    if (config.DebugChatTap) chat.Print($"[AI Companion:{route}] → {contPrefix}{chunk}");
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

            var prefix = first ? firstPrefix : contPrefix;
            await RunOnFrameworkAsync(() => commands.ProcessCommand(cmd + prefix + chunk), token).ConfigureAwait(false);
            if (config.DebugChatTap) chat.Print($"[AI Companion:{route}] → {prefix}{chunk}");
            first = false;
            await Task.Delay(delay, token).ConfigureAwait(false);
        }

        return true;
    }

    // ---------- helpers ----------
    private async Task RunOnFrameworkAsync(Action action, CancellationToken token)
    {
        var tcs = new TaskCompletionSource();
        framework.RunOnFrameworkThread(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        using (token.Register(() => tcs.TrySetCanceled(token)))
            await tcs.Task.ConfigureAwait(false);
    }

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

    internal async Task SendToPartyAsync(string text, CancellationToken token)
    {
        throw new NotImplementedException();
    }
}
