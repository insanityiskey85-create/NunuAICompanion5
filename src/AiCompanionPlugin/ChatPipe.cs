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

/// <summary>
/// Routes text into game chat with multiple fallbacks:
/// 1) ChatGui.Send(string) / SendChat(string) via reflection
/// 2) ChatGui.SendMessage(XivChatType, string) via reflection
/// 3) ICommandManager.DispatchCommand / ProcessCommand for "/s" and "/p"
///
/// Handles chunking, sentence-aware flushing, and sanitation to avoid swallow.
/// Requires OutboundDispatcher (separate file).
/// </summary>
public sealed class ChatPipe : IDisposable
{
    private readonly ICommandManager commands;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly IChatGui chat;
    private readonly OutboundDispatcher dispatcher;

    private readonly MethodInfo? cmDispatch;
    private readonly MethodInfo? cmProcess;

    private readonly MethodInfo? chatSend;   // ChatGui.SendMessage(string) or SendChat(string)
    private readonly MethodInfo? chatSend2;  // ChatGui.SendMessage(XivChatType, string)

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
        cmDispatch = GetMethod(cmType, "DispatchCommand", new[] { typeof(string) })
                     ?? GetMethod(cmType, "DispatchCommand", new[] { typeof(string), typeof(bool) })
                     ?? GetMethod(cmType, "DispatchCommand", new[] { typeof(string), typeof(bool?), typeof(bool?) });
        cmProcess = GetMethod(cmType, "ProcessCommand", new[] { typeof(string) });

        var chatType = chat.GetType();
        chatSend = GetMethod(chatType, "SendMessage", new[] { typeof(string) })
                    ?? GetMethod(chatType, "SendChat", new[] { typeof(string) }); // older private path

        chatSend2 = chatType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(m =>
            {
                if (m.Name != "SendMessage") return false;
                var p = m.GetParameters();
                return p.Length == 2 && p[1].ParameterType == typeof(string);
            });

        log.Info($"[ChatPipe] CMD(Dispatch={cmDispatch != null}, Process={cmProcess != null}) | CHAT(Send1={chatSend != null}, Send2={chatSend2 != null})");
    }

    private static MethodInfo? GetMethod(Type t, string name, Type[] sig) =>
        t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, binder: null, types: sig, modifiers: null);

    private (string cmdPrefix, int chunk, int delay, int flushChars, int flushMs, bool enabled, string aiPrefix, ChatRoute route) GetRouteParams(ChatRoute route)
    {
        var aiName = string.IsNullOrWhiteSpace(config.AiDisplayName) ? "AI Nunu" : config.AiDisplayName;
        var aiPrefix = config.UseAsciiHeaders ? $"[{aiName}] " : $"[{aiName}] ";

        return route switch
        {
            ChatRoute.Party => ("/p ", Math.Max(220, config.PartyChunkSize), Math.Max(300, config.PartyPostDelayMs),
                                Math.Max(200, config.PartyStreamFlushChars), Math.Max(700, config.PartyStreamMinFlushMs),
                                config.EnablePartyPipe, aiPrefix, route),
            ChatRoute.Say => ("/s ", Math.Max(220, config.SayChunkSize), Math.Max(300, config.SayPostDelayMs),
                                Math.Max(200, config.SayStreamFlushChars), Math.Max(700, config.SayStreamMinFlushMs),
                                config.EnableSayPipe, aiPrefix, route),
            _ => ("/s ", 280, 300, 200, 700, false, aiPrefix, route),
        };
    }

    // ---------- Public API ----------

    public Task<bool> SendToAsync(ChatRoute route, string? text, CancellationToken token = default, bool addPrefix = true)
    {
        var (cmd, chunkSize, delay, _, _, enabled, aiPrefix, r) = GetRouteParams(route);
        if (!enabled || string.IsNullOrWhiteSpace(text)) return Task.FromResult(false);

        var lines = PrepareForNetwork(text!, addPrefix ? aiPrefix : string.Empty, chunkSize);
        return SendLinesAsync(cmd, lines, r, delay, token);
    }

    public async Task<bool> SendStreamingToAsync(ChatRoute route, IAsyncEnumerable<string> tokens, string headerForFirstLine, CancellationToken token = default)
    {
        var (cmd, chunkSize, delay, flushCharsCfg, flushMsCfg, enabled, aiPrefix, r) = GetRouteParams(route);
        if (!enabled) return false;

        string firstPrefix = aiPrefix + (headerForFirstLine ?? string.Empty);
        string contPrefix = "… ";

        var firstCap = Math.Max(120, chunkSize - firstPrefix.Length);
        var contCap = Math.Max(120, chunkSize - contPrefix.Length);

        var sb = new StringBuilder(1024);
        var first = true;
        var lastFlush = Environment.TickCount64;
        var minFlushMs = Math.Max(500, flushMsCfg);
        var hardFlushMs = Math.Max(minFlushMs + 600, 1600);

        await foreach (var t in tokens.WithCancellation(token))
        {
            if (!string.IsNullOrEmpty(t)) sb.Append(t);

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
                    var line = San((first ? firstPrefix : contPrefix) + chunk);
                    await SendLineAsync(cmd, line, r, token);
                    first = false;
                    lastFlush = Environment.TickCount64;
                    cap = contCap;
                    reachedCap = sb.Length >= cap - 8;
                    endSentence = sb.Length >= 60 && EndsWithSentence(sb);
                    idleTimeout = false;
                }
                await Task.Delay(delay, token);
            }
        }

        var finalCap = first ? firstCap : contCap;
        while (sb.Length > 0)
        {
            token.ThrowIfCancellationRequested();
            var chunk = TakeUntil(sb, finalCap);
            var line = San((first ? firstPrefix : contPrefix) + chunk);
            await SendLineAsync(cmd, line, r, token);
            first = false;
            finalCap = contCap;
            await Task.Delay(80, token);
        }
        return true;
    }

    // ---------- Core send ----------

    private async Task<bool> SendLinesAsync(string cmdPrefix, IEnumerable<string> lines, ChatRoute route, int delay, CancellationToken token)
    {
        bool any = false;
        foreach (var line in lines)
        {
            token.ThrowIfCancellationRequested();
            await SendLineAsync(cmdPrefix, line, route, token);
            await Task.Delay(delay, token);
            any = true;
        }
        return any;
    }

    private Task SendLineAsync(string cmdPrefix, string line, ChatRoute route, CancellationToken token)
    {
        var text = San(line);
        var full = (cmdPrefix + text).Trim();

        var tcs = new TaskCompletionSource();
        dispatcher.Enqueue(() =>
        {
            try
            {
                // Prefer ChatGui path if configured; otherwise commands first.
                var preferChat = config.PreferChatGuiSend;
                bool ok =
                    (preferChat && (TryChatSendString(full) || TryChatSendTyped(cmdPrefix, text))) ||
                    TryCommandDispatch(full) ||
                    (!preferChat && (TryChatSendString(full) || TryChatSendTyped(cmdPrefix, text)));

                if (!ok)
                    throw new InvalidOperationException("No available chat send path succeeded.");

                OnEcho(route, text);
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        using (token.Register(() => tcs.TrySetCanceled(token)))
            return tcs.Task;
    }

    // ---------- Paths ----------

    private bool TryCommandDispatch(string full)
    {
        try
        {
            if (cmDispatch != null)
            {
                var p = cmDispatch.GetParameters();
                if (p.Length == 1) cmDispatch.Invoke(commands, new object[] { full });
                else if (p.Length == 2) cmDispatch.Invoke(commands, new object[] { full, false });
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
            log.Error(tex.InnerException ?? tex, "Process/Dispatch threw");
        }
        catch (Exception ex)
        {
            log.Error(ex, "Process/Dispatch failed");
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

            // Resolve enum names safely (API can vary)
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

    // ---------- Helpers ----------

    private IEnumerable<string> PrepareForNetwork(string text, string prefix, int chunkSize)
    {
        // Brutal sanitation: remove newlines, replace dangerous braces, trim, optional ASCII-only
        var s = (text ?? string.Empty).Replace("\r\n", " ").Replace("\n", " ");
        s = s.Replace("[", "［").Replace("]", "］").Replace("{", "(").Replace("}", ")");
        s = System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ").Trim();
        if (config.NetworkAsciiOnly)
            s = StripNonAscii(s);

        var max = Math.Max(180, chunkSize - prefix.Length);
        foreach (var chunk in ChunkSmart(s, max))
            yield return prefix + chunk;
    }

    private static bool EndsWithSentence(StringBuilder sb)
    {
        int i = sb.Length - 1;
        while (i >= 0 && char.IsWhiteSpace(sb[i])) i--;
        if (i < 0) return false;
        char c = sb[i];
        if (c is '.' or '!' or '?' or '…') return true;
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
        if (sb.Length <= cap)
        {
            var all = sb.ToString();
            sb.Clear();
            return all;
        }
        int cut = cap;
        for (int i = cap; i >= Math.Max(0, cap - 40); i--)
            if (char.IsWhiteSpace(sb[i])) { cut = i; break; }

        var s = sb.ToString(0, cut).TrimEnd();
        sb.Remove(0, cut);
        return s;
    }

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
            else sb.Append(' ');
        }
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "\\s+", " ").Trim();
    }

    private static string San(string s)
    {
        if (s == null) return string.Empty;
        s = s.Replace("\r\n", " ").Replace("\n", " ");
        return System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ").Trim();
    }

    public void Dispose() => dispatcher.Dispose();
}
