using System;
using System.Collections.Generic;
using System.Linq;
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

    private readonly MethodInfo? cmDispatch;
    private readonly MethodInfo? cmProcess;
    private readonly MethodInfo? chatSend;
    private readonly MethodInfo? chatSend2;

    public ChatPipe(ICommandManager commands, IPluginLog log, Configuration config, IFramework framework, IChatGui chat)
    {
        this.commands = commands;
        this.log = log;
        this.config = config;
        this.chat = chat;

        dispatcher = new OutboundDispatcher(framework, log)
        {
            MinIntervalMs = Math.Max(200, config.SayPostDelayMs / 2)
        };

        var cmType = commands.GetType();
        cmDispatch = FindMethod(cmType, "DispatchCommand", new[] { typeof(string) })
                     ?? FindMethod(cmType, "DispatchCommand", new[] { typeof(string), typeof(bool) })
                     ?? FindMethod(cmType, "DispatchCommand", new[] { typeof(string), typeof(bool?), typeof(bool?) });
        cmProcess = FindMethod(cmType, "ProcessCommand", new[] { typeof(string) });

        var chatType = chat.GetType();
        chatSend = FindMethod(chatType, "SendMessage", new[] { typeof(string) })
                    ?? FindMethod(chatType, "SendChat", new[] { typeof(string) });

        chatSend2 = chatType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(m =>
            {
                if (m.Name != "SendMessage") return false;
                var p = m.GetParameters();
                return p.Length == 2 && p[1].ParameterType == typeof(string);
            });

        log.Info($"ChatPipe init → CMD[Dispatch={cmDispatch != null}, Process={cmProcess != null}] | CHAT[Send1={chatSend != null}, Send2={chatSend2 != null}]");
    }

    private static MethodInfo? FindMethod(Type t, string name, Type[] sig) =>
        t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, sig, null);

    // Use universal aliases: /p and /s
    private (string cmdPrefix, int chunk, int delay, int flushChars, int flushMs, bool enabled, string aiPrefix, ChatRoute route) GetRouteParams(ChatRoute route)
    {
        var aiPrefix = string.IsNullOrWhiteSpace(config.AiDisplayName) ? "[AI Nunu] " : $"[{config.AiDisplayName}] ";
        return route switch
        {
            ChatRoute.Party => ("/p ", Math.Max(240, config.PartyChunkSize), Math.Max(250, config.PartyPostDelayMs),
                                Math.Max(220, config.PartyStreamFlushChars), Math.Max(700, config.PartyStreamMinFlushMs),
                                config.EnablePartyPipe, aiPrefix, route),
            ChatRoute.Say => ("/s ", Math.Max(240, config.SayChunkSize), Math.Max(250, config.SayPostDelayMs),
                                Math.Max(220, config.SayStreamFlushChars), Math.Max(700, config.SayStreamMinFlushMs),
                                config.EnableSayPipe, aiPrefix, route),
            _ => ("/s ", 360, 300, 220, 700, false, aiPrefix, route),
        };
    }

    public async Task<bool> SendToAsync(ChatRoute route, string? text, CancellationToken token = default, bool addPrefix = true)
    {
        var (cmd, chunkSize, delay, _, _, enabled, aiPrefix, r) = GetRouteParams(route);
        if (!enabled) { log.Info($"ChatPipe({r}): pipe disabled."); return false; }
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var line in PrepareForNetwork(text, addPrefix ? aiPrefix : string.Empty, chunkSize))
        {
            token.ThrowIfCancellationRequested();
            await SendLineAsync(cmd, line, r, token).ConfigureAwait(false);
            await Task.Delay(delay, token).ConfigureAwait(false);
        }
        return true;
    }

    public async Task<bool> SendStreamingToAsync(ChatRoute route, IAsyncEnumerable<string> tokens, string headerForFirstLine, CancellationToken token = default)
    {
        var (cmd, chunkSize, delay, flushCharsCfg, flushMsCfg, enabled, aiPrefix, r) = GetRouteParams(route);
        if (!enabled) { log.Info($"ChatPipe({r}): pipe disabled."); return false; }

        headerForFirstLine ??= string.Empty;

        var firstPrefix = aiPrefix + headerForFirstLine;
        var contPrefix = "… ";

        var firstCap = Math.Max(120, chunkSize - GetLen(firstPrefix));
        var contCap = Math.Max(120, chunkSize - GetLen(contPrefix));

        var sb = new StringBuilder(1024);
        var first = true;

        var minFlushMs = Math.Max(500, flushMsCfg);
        var hardFlushMs = Math.Max(minFlushMs + 600, 1600);
        var lastFlush = Environment.TickCount64;

        await foreach (var t in tokens.WithCancellation(token))
        {
            if (!string.IsNullOrEmpty(t))
                sb.Append(t);

            var elapsed = (int)(Environment.TickCount64 - lastFlush);
            var cap = first ? firstCap : contCap;
            var reachedCap = sb.Length >= cap - 8;
            var endSentence = sb.Length >= 60 && EndsWithSentence(sb);
            var idleTimeout = elapsed >= hardFlushMs;
            var minWindow = elapsed >= minFlushMs;

            if ((reachedCap || endSentence || idleTimeout) && (minWindow || reachedCap || idleTimeout))
            {
                while (sb.Length > 0 && (reachedCap || endSentence || idleTimeout))
                {
                    token.ThrowIfCancellationRequested();
                    var chunk = TakeUntil(sb, cap);
                    await SendLineAsync(cmd, San((first ? firstPrefix : contPrefix) + chunk), r, token).ConfigureAwait(false);
                    first = false;
                    lastFlush = Environment.TickCount64;

                    cap = contCap;
                    reachedCap = sb.Length >= cap - 8;
                    endSentence = sb.Length >= 60 && EndsWithSentence(sb);
                    idleTimeout = false;
                }
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
        }

        var finalCap = first ? firstCap : contCap;
        while (sb.Length > 0)
        {
            token.ThrowIfCancellationRequested();
            var chunk = TakeUntil(sb, finalCap);
            await SendLineAsync(cmd, San((first ? firstPrefix : contPrefix) + chunk), r, token).ConfigureAwait(false);
            first = false;
            finalCap = contCap;
            await Task.Delay(80, token).ConfigureAwait(false);
        }

        return true;
    }

    private Task SendLineAsync(string cmdPrefix, string line, ChatRoute route, CancellationToken token)
    {
        var text = San(line);
        var full = (cmdPrefix + text).Trim(); // trim leading spaces that can cancel dispatch

        var tcs = new TaskCompletionSource();
        dispatcher.Enqueue(() =>
        {
            try
            {
                if (TryCommandDispatch(full) || TryChatSendString(full) || TryChatSendTyped(cmdPrefix, text))
                {
                    OnEcho(route, text);
                    tcs.TrySetResult();
                    return;
                }
                throw new InvalidOperationException("No available chat send path (Dispatch/Process/ChatGui) succeeded.");
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        using (token.Register(() => tcs.TrySetCanceled(token)))
            return tcs.Task;
    }

    private bool TryCommandDispatch(string full)
    {
        try
        {
            if (cmDispatch != null)
            {
                var pars = cmDispatch.GetParameters();
                if (pars.Length == 1) cmDispatch.Invoke(commands, new object[] { full });
                else if (pars.Length == 2) cmDispatch.Invoke(commands, new object[] { full, false });
                else cmDispatch.Invoke(commands, new object[] { full, false, false });
                log.Info("ChatPipe → ICommandManager.DispatchCommand()");
                return true;
            }
            if (cmProcess != null)
            {
                cmProcess.Invoke(commands, new object[] { full });
                log.Info("ChatPipe → ICommandManager.ProcessCommand()");
                return true;
            }
        }
        catch (TargetInvocationException tex)
        {
            log.Error(tex.InnerException ?? tex, "Dispatch/ProcessCommand threw");
        }
        catch (Exception ex)
        {
            log.Error(ex, "Dispatch/ProcessCommand failed");
        }
        return false;
    }

    private bool TryChatSendString(string full)
    {
        try
        {
            if (chatSend != null)
            {
                chatSend.Invoke(chat, new object[] { full });
                log.Info("ChatPipe → ChatGui.Send(string)");
                return true;
            }
        }
        catch (TargetInvocationException tex)
        {
            log.Error(tex.InnerException ?? tex, "ChatGui.Send(string) threw");
        }
        catch (Exception ex)
        {
            log.Error(ex, "ChatGui.Send(string) failed");
        }
        return false;
    }

    private bool TryChatSendTyped(string cmdPrefix, string text)
    {
        try
        {
            if (chatSend2 == null) return false;

            var pars = chatSend2.GetParameters();
            var enumType = pars[0].ParameterType;

            var isSay = cmdPrefix.StartsWith("/s", StringComparison.OrdinalIgnoreCase);
            var nameSay = Enum.GetNames(enumType).FirstOrDefault(n => n.Equals("Say", StringComparison.OrdinalIgnoreCase));
            var nameParty = Enum.GetNames(enumType).FirstOrDefault(n => n.Equals("Party", StringComparison.OrdinalIgnoreCase));
            var chosenName = isSay ? nameSay : nameParty;
            if (chosenName == null) return false;

            var channel = Enum.Parse(enumType, chosenName);
            chatSend2.Invoke(chat, new object[] { channel!, text });
            log.Info("ChatPipe → ChatGui.SendMessage(XivChatType, string)");
            return true;
        }
        catch (TargetInvocationException tex)
        {
            log.Error(tex.InnerException ?? tex, "ChatGui.Send(typed) threw");
        }
        catch (Exception ex)
        {
            log.Error(ex, "ChatGui.Send(typed) failed");
        }
        return false;
    }

    private void OnEcho(ChatRoute route, string text)
    {
        if (config.DebugChatTap)
            chat.Print($"[AI Companion:{(route == ChatRoute.Party ? "Party" : "Say")}] → {text}");
        log.Info($"ChatPipe({(route == ChatRoute.Party ? "Party" : "Say")}) sent {text.Length} chars.");
    }

    // ---------- helpers ----------

    private IEnumerable<string> PrepareForNetwork(string text, string prefix, int chunkSize)
    {
        text = (text ?? string.Empty).Replace("\r\n", " ").Replace("\n", " ").Trim();
        var max = Math.Max(200, chunkSize - GetLen(prefix));
        foreach (var chunk in ChunkSmart(text, max))
            yield return prefix + chunk;
    }

    private static bool EndsWithSentence(StringBuilder sb)
    {
        int i = sb.Length - 1;
        while (i >= 0 && char.IsWhiteSpace(sb[i])) i--;
        if (i < 0) return false;
        char c = sb[i];
        if (c is '.' or '!' or '?' or '…') return true;
        if (c is '"' or '\'' or ')' or ']' or '»') { i--; if (i >= 0) { c = sb[i]; if (c is '.' or '!' or '?' or '…') return true; } }
        return false;
    }

    private static string TakeUntil(StringBuilder sb, int cap)
    {
        if (sb.Length <= cap) { var all = sb.ToString(); sb.Clear(); return all; }
        int cut = cap;
        for (int i = cap; i >= Math.Max(0, cap - 40); i--) if (char.IsWhiteSpace(sb[i])) { cut = i; break; }
        var s = sb.ToString(0, cut).TrimEnd();
        sb.Remove(0, cut);
        return s;
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
                for (int i = 0; i < w.Length; i += max) yield return w.Substring(i, Math.Min(max, w.Length - i));
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

    private static string San(string s) => s == null ? string.Empty : (s.Replace("\r\n", " ").Replace("\n", " ").Trim());

    public void Dispose() => dispatcher.Dispose();
}
