using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

public enum ChatRoute { Party, Say }

/// <summary>
/// Outbound router to /party or /say. Enqueues actual sends to OutboundDispatcher,
/// so they occur on a clean Framework tick. Uses direct ChatGui methods if present,
/// otherwise falls back to ICommandManager.ProcessCommand.
/// </summary>
public sealed class ChatPipe
{
    private readonly ICommandManager commands;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly IChatGui chat;
    private readonly OutboundDispatcher dispatcher;

    // reflective method handles (cached)
    private readonly MethodInfo? sendChat;
    private readonly MethodInfo? sendMessage;

    public OutboundDispatcher Dispatcher => dispatcher;

    public ChatPipe(ICommandManager commands, IPluginLog log, Configuration config, IFramework framework, IChatGui chat)
    {
        this.commands = commands;
        this.log = log;
        this.config = config;
        this.chat = chat;
        this.dispatcher = new OutboundDispatcher(framework, log) { MinIntervalMs = Math.Max(200, config.SayPostDelayMs / 2) };

        // Try to discover a direct send method on ChatGui (API variants differ).
        var implType = chat.GetType();
        sendChat = implType.GetMethod("SendChat", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, new Type[] { typeof(string) });
        sendMessage = implType.GetMethod("SendMessage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, new Type[] { typeof(string) });
        if (sendChat != null) log.Info("ChatPipe: using ChatGui.SendChat for outbound messages.");
        else if (sendMessage != null) log.Info("ChatPipe: using ChatGui.SendMessage for outbound messages.");
        else log.Info("ChatPipe: no direct ChatGui send method found; will use ProcessCommand.");
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

    /// <summary>Send a whole message to the route, chunked. Returns false if route disabled.</summary>
    public async Task<bool> SendToAsync(ChatRoute route, string? text, CancellationToken token = default, bool addPrefix = true)
    {
        var (cmd, chunkSize, delay, _, _, enabled, aiPrefix) = GetRouteParams(route);
        if (!enabled) { log.Info($"ChatPipe({route}): pipe disabled."); return false; }
        if (string.IsNullOrWhiteSpace(text)) return false;

        var payload = PrepareForNetwork(text, addPrefix ? aiPrefix : string.Empty, chunkSize);
        foreach (var line in payload)
        {
            token.ThrowIfCancellationRequested();
            await SendLineAsync(cmd, line, token).ConfigureAwait(false);
            await Task.Delay(delay, token).ConfigureAwait(false);
        }
        return true;
    }

    /// <summary>Stream tokens to route with header on first line and continuation thereafter.</summary>
    public async Task<bool> SendStreamingToAsync(ChatRoute route, IAsyncEnumerable<string> tokens, string headerForFirstLine, CancellationToken token = default)
    {
        var (cmd, chunkSize, delay, flushChars, flushMs, enabled, aiPrefix) = GetRouteParams(route);
        if (!enabled) { log.Info($"ChatPipe({route}): pipe disabled."); return false; }

        headerForFirstLine ??= string.Empty;
        var firstPrefix = aiPrefix + headerForFirstLine;
        var contPrefix = "…";

        var firstMax = Math.Max(64, chunkSize - GetLen(firstPrefix));
        var contMax = Math.Max(64, chunkSize - GetLen(contPrefix));

        var sb = new StringBuilder();
        var first = true;
        var lastFlush = Environment.TickCount;

        await foreach (var t in tokens.WithCancellation(token))
        {
            if (string.IsNullOrEmpty(t)) continue;
            sb.Append(t);

            var elapsed = Environment.TickCount - lastFlush;
            var needFlush = sb.Length >= Math.Max(40, flushChars) || elapsed >= Math.Max(300, flushMs);

            while (needFlush && sb.Length > 0)
            {
                token.ThrowIfCancellationRequested();

                if (first)
                {
                    var chunk = TakeChunk(sb, firstMax);
                    await SendLineAsync(cmd, San(firstPrefix) + San(chunk), token).ConfigureAwait(false);
                    first = false;
                }
                else
                {
                    var chunk = TakeChunk(sb, contMax);
                    await SendLineAsync(cmd, San(contPrefix) + San(chunk), token).ConfigureAwait(false);
                }

                lastFlush = Environment.TickCount;
                await Task.Delay(delay, token).ConfigureAwait(false);

                needFlush = sb.Length >= Math.Max(40, flushChars) || (Environment.TickCount - lastFlush) >= Math.Max(300, flushMs);
            }
        }

        // Final flush
        while (sb.Length > 0)
        {
            token.ThrowIfCancellationRequested();
            var prefix = first ? firstPrefix : contPrefix;
            var cap = first ? firstMax : contMax;
            var chunk = TakeChunk(sb, cap);
            await SendLineAsync(cmd, San(prefix) + San(chunk), token).ConfigureAwait(false);
            first = false;
            await Task.Delay(delay, token).ConfigureAwait(false);
        }

        return true;
    }

    // ---- core send path (queued to dispatcher) ----
    private Task SendLineAsync(string cmdPrefix, string line, CancellationToken token)
    {
        var text = line;
        if (config.NetworkAsciiOnly) text = StripNonAscii(text);

        var full = cmdPrefix + text;

        var tcs = new TaskCompletionSource();
        Dispatcher.Enqueue(() =>
        {
            try
            {
                // Try direct ChatGui method first
                if (sendChat != null)
                {
                    var arg = text.StartsWith("/") ? text : full;
                    sendChat.Invoke(chat, new object[] { arg });
                }
                else if (sendMessage != null)
                {
                    var arg = text.StartsWith("/") ? text : full;
                    sendMessage.Invoke(chat, new object[] { arg });
                }
                else
                {
                    commands.ProcessCommand(full);
                }

                if (config.DebugChatTap)
                    chat.Print($"[AI Companion:{(cmdPrefix.StartsWith("/party") ? "Party" : "Say")}] → {line}");

                log.Info($"ChatPipe({(cmdPrefix.StartsWith("/party") ? "Party" : "Say")}) sent {line.Length} chars.");
                tcs.TrySetResult();
            }
            catch (TargetInvocationException ex)
            {
                log.Error(ex.InnerException ?? ex, "ChatPipe reflective send failed; falling back to ProcessCommand.");
                try
                {
                    commands.ProcessCommand(full);
                    tcs.TrySetResult();
                }
                catch (Exception ex2)
                {
                    tcs.TrySetException(ex2);
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        using (token.Register(() => tcs.TrySetCanceled(token)))
            return tcs.Task;
    }

    // ---- chunking + sanitizing helpers ----
    private IEnumerable<string> PrepareForNetwork(string text, string prefix, int chunkSize)
    {
        text = text.Replace("\r\n", "\n").Trim();
        var chunks = ChunkSmart(text, Math.Max(64, chunkSize - GetLen(prefix)));
        bool first = true;
        foreach (var c in chunks)
        {
            yield return (first ? prefix : "…") + c;
            first = false;
        }
    }

    private static string TakeChunk(StringBuilder sb, int max)
    {
        var take = Math.Min(max, sb.Length);
        var s = sb.ToString(0, take);
        sb.Remove(0, take);
        return s;
    }

    private static int GetLen(string s) => s?.Length ?? 0;

    private static IEnumerable<string> ChunkSmart(string text, int max)
    {
        if (text.Length <= max) { yield return text; yield break; }
        var sentences = SplitSentences(text);
        var sb = new StringBuilder();
        foreach (var s in sentences)
        {
            if (s.Length > max)
            {
                foreach (var w in ChunkWords(s, max)) yield return w;
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
                for (int i = 0; i < w.Length; i += max)
                    yield return w.Substring(i, Math.Min(max, w.Length - i));
            }
            else if (sb.Length + w.Length + 1 > max)
            {
                yield return sb.ToString(); sb.Clear(); sb.Append(w);
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
            if (i < parts.Length - 1 && !t.Contains('\n')) seg += ".";
            yield return seg;
        }
    }

    private static string StripNonAscii(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (ch >= 32 && ch <= 126) sb.Append(ch);
            else if (ch == '\n') sb.Append(' ');
        }
        return sb.ToString();
    }

    private static string San(string s) => s == null ? string.Empty : StripNonAscii(s);

    public void Dispose() => Dispatcher.Dispose();

    internal async Task SendToPartyAsync(string text, CancellationToken token)
    {
        throw new NotImplementedException();
    }
}
