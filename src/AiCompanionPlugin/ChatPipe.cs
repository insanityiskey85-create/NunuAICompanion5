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

public sealed class ChatPipe : IDisposable
{
    private readonly ICommandManager commands;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly IChatGui chat;
    private readonly OutboundDispatcher dispatcher;

    private readonly MethodInfo? sendChat;
    private readonly MethodInfo? sendMessage;

    public ChatPipe(ICommandManager commands, IPluginLog log, Configuration config, IFramework framework, IChatGui chat)
    {
        this.commands = commands;
        this.log = log;
        this.config = config;
        this.chat = chat;
        this.dispatcher = new OutboundDispatcher(framework, log) { MinIntervalMs = Math.Max(200, config.SayPostDelayMs / 2) };

        var implType = chat.GetType();
        sendChat = implType.GetMethod("SendChat", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, new Type[] { typeof(string) });
        sendMessage = implType.GetMethod("SendMessage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, new Type[] { typeof(string) });
    }

    private (string cmdPrefix, int chunk, int delay, int flushChars, int flushMs, bool enabled, string aiPrefix) GetRouteParams(ChatRoute route)
    {
        var aiPrefix = string.IsNullOrWhiteSpace(config.AiDisplayName) ? "[AI Nunu] " : $"[{config.AiDisplayName}] ";
        return route switch
        {
            ChatRoute.Party => ("/party ", Math.Max(240, config.PartyChunkSize), Math.Max(250, config.PartyPostDelayMs),
                                Math.Max(220, config.PartyStreamFlushChars), Math.Max(700, config.PartyStreamMinFlushMs),
                                config.EnablePartyPipe, aiPrefix),
            ChatRoute.Say => ("/say ", Math.Max(240, config.SayChunkSize), Math.Max(250, config.SayPostDelayMs),
                                Math.Max(220, config.SayStreamFlushChars), Math.Max(700, config.SayStreamMinFlushMs),
                                config.EnableSayPipe, aiPrefix),
            _ => ("/say ", 360, 300, 220, 700, false, aiPrefix),
        };
    }

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

    /// <summary>
    /// STREAMING: buffer tokens and flush full sentences or near-max chunks.
    /// This prevents one-word spam and keeps lines readable.
    /// </summary>
    public async Task<bool> SendStreamingToAsync(ChatRoute route, IAsyncEnumerable<string> tokens, string headerForFirstLine, CancellationToken token = default)
    {
        var (cmd, chunkSize, delay, flushCharsCfg, flushMsCfg, enabled, aiPrefix) = GetRouteParams(route);
        if (!enabled) { log.Info($"ChatPipe({route}): pipe disabled."); return false; }

        headerForFirstLine ??= string.Empty;

        // Prefixes for the first/continuation lines
        var firstPrefix = aiPrefix + headerForFirstLine;
        var contPrefix = "… ";

        // Max payload per line after prefix
        var firstCap = Math.Max(120, chunkSize - GetLen(firstPrefix));
        var contCap = Math.Max(120, chunkSize - GetLen(contPrefix));

        // Sentence-aware buffer
        var sb = new StringBuilder(1024);
        var first = true;

        // Flush policy
        var minFlushMs = Math.Max(500, flushMsCfg);     // wait a bit to gather words
        var hardFlushMs = Math.Max(minFlushMs + 600, 1600); // force flush if backend trickles too slowly
        var lastFlush = Environment.TickCount64;
        var lastToken = Environment.TickCount64;

        await foreach (var t in tokens.WithCancellation(token))
        {
            if (string.IsNullOrEmpty(t)) continue;
            sb.Append(t);
            lastToken = Environment.TickCount64;

            // Decide if we should flush now
            var lineCap = first ? firstCap : contCap;
            var length = sb.Length;
            var elapsed = (int)(Environment.TickCount64 - lastFlush);

            bool reachedCap = length >= lineCap - 8; // small safety margin
            bool endSentence = length >= 60 && EndsWithSentence(sb); // decent sentence
            bool idleTimeout = elapsed >= hardFlushMs;               // too long without flush
            bool minWindowMet = elapsed >= minFlushMs;

            if ((reachedCap || endSentence || idleTimeout) && (minWindowMet || reachedCap || idleTimeout))
            {
                // spill as much as possible up to line cap
                while (sb.Length > 0 && (reachedCap || endSentence || idleTimeout))
                {
                    var chunk = TakeUntil(sb, lineCap);
                    await SendLineAsync(cmd, San((first ? firstPrefix : contPrefix) + chunk), token).ConfigureAwait(false);
                    first = false;
                    lastFlush = Environment.TickCount64;

                    // re-evaluate after sending
                    lineCap = contCap;
                    reachedCap = sb.Length >= lineCap - 8;
                    endSentence = sb.Length >= 60 && EndsWithSentence(sb);
                    idleTimeout = false; // reset after a send
                }

                await Task.Delay(delay, token).ConfigureAwait(false);
            }
        }

        // Final spill of remaining buffer (may be multiple lines if long)
        var finalCap = first ? firstCap : contCap;
        while (sb.Length > 0)
        {
            var chunk = TakeUntil(sb, finalCap);
            await SendLineAsync(cmd, San((first ? firstPrefix : contPrefix) + chunk), token).ConfigureAwait(false);
            first = false;
            finalCap = contCap;
            await Task.Delay(80, token).ConfigureAwait(false);
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
        dispatcher.Enqueue(() =>
        {
            try
            {
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
                    chat.Print($"[AI Companion:{(cmdPrefix.StartsWith("/party") ? "Party" : "Say")}] → {text}");

                log.Info($"ChatPipe({(cmdPrefix.StartsWith("/party") ? "Party" : "Say")}) sent {text.Length} chars.");
                tcs.TrySetResult();
            }
            catch (TargetInvocationException ex)
            {
                log.Error(ex.InnerException ?? ex, "ChatPipe reflective send failed; falling back to ProcessCommand.");
                try { commands.ProcessCommand(full); tcs.TrySetResult(); }
                catch (Exception ex2) { tcs.TrySetException(ex2); }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        using (token.Register(() => tcs.TrySetCanceled(token)))
            return tcs.Task;
    }

    // ---- helpers ----
    private static bool EndsWithSentence(StringBuilder sb)
    {
        // look at last 1–3 non-space chars
        int i = sb.Length - 1;
        while (i >= 0 && char.IsWhiteSpace(sb[i])) i--;
        if (i < 0) return false;

        char c = sb[i];
        if (c is '.' or '!' or '?' or '…') return true;
        // handle “).” or “!” after quote
        if (c is '"' or '\'' or ')' or ']' or '»')
        {
            i--;
            if (i >= 0)
            {
                c = sb[i];
                if (c is '.' or '!' or '?' or '…') return true;
            }
        }
        return false;
    }

    private static string TakeUntil(StringBuilder sb, int cap)
    {
        if (sb.Length <= cap) { var all = sb.ToString(); sb.Clear(); return all; }

        // Try to cut at last whitespace before cap
        int cut = cap;
        for (int i = cap; i >= Math.Max(0, cap - 40); i--)
        {
            if (char.IsWhiteSpace(sb[i])) { cut = i; break; }
        }
        var s = sb.ToString(0, cut).TrimEnd();
        sb.Remove(0, cut);
        return s;
    }

    private IEnumerable<string> PrepareForNetwork(string text, string prefix, int chunkSize)
    {
        text = text.Replace("\r\n", "\n").Trim();
        var max = Math.Max(200, chunkSize - GetLen(prefix));
        foreach (var chunk in ChunkSmart(text, max))
            yield return (prefix + chunk);
    }

    private static int GetLen(string s) => s?.Length ?? 0;

    private static IEnumerable<string> ChunkSmart(string text, int max)
    {
        if (text.Length <= max) { yield return text; yield break; }
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var w in words)
        {
            if (sb.Length + w.Length + 1 > max)
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
            }
            if (w.Length > max)
            {
                for (int i = 0; i < w.Length; i += max)
                    yield return w.Substring(i, Math.Min(max, w.Length - i));
            }
            else
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(w);
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
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

    public void Dispose() => dispatcher.Dispose();
}
