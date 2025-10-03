using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Plugin;
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
    private readonly ChatTwoBridge? bridge;

    // reflection targets (nullable; we probe at runtime)
    private readonly MethodInfo? cmDispatch;
    private readonly MethodInfo? cmProcess;

    private readonly MethodInfo? chatSendString;
    private readonly MethodInfo? chatSendTyped;
    private readonly MethodInfo? chatOpenInput;

    public ChatPipe(ICommandManager commands, IPluginLog log, Configuration config, IFramework framework, IChatGui chat, IDalamudPluginInterface pi)
    {
        this.commands = commands;
        this.log = log;
        this.config = config;
        this.chat = chat;

        dispatcher = new OutboundDispatcher(framework, log)
        {
            MinIntervalMs = Math.Max(200, config.SayPostDelayMs / 2)
        };

        // optional IPC bridge
        try { bridge = new ChatTwoBridge(pi, log); }
        catch (Exception ex) { log.Error(ex, "[ChatPipe] ChatTwoBridge init failed"); bridge = null; }

        // ICommandManager reflection
        var cmType = commands.GetType();
        cmDispatch = GetMethod(cmType, "DispatchCommand", new[] { typeof(string) })
                  ?? GetMethod(cmType, "DispatchCommand", new[] { typeof(string), typeof(bool) })
                  ?? GetMethod(cmType, "DispatchCommand", new[] { typeof(string), typeof(bool?), typeof(bool?) });
        cmProcess = GetMethod(cmType, "ProcessCommand", new[] { typeof(string) });

        // IChatGui reflection (different Dalamud builds rename these)
        var chatType = chat.GetType();
        chatSendString = FindStringSender(chatType);
        chatSendTyped = FindTypedSender(chatType);
        chatOpenInput = FindOpenChat(chatType);

        log.Info($"[ChatPipe] CMD(Dispatch={(cmDispatch != null)}, Process={(cmProcess != null)}) | " +
                 $"CHAT(String={chatSendString?.Name ?? "–"}, Typed={chatSendTyped?.Name ?? "–"}, OpenChat={chatOpenInput?.Name ?? "–"}) | " +
                 $"IPC={(bridge is { IsAvailable: true } ? "ChatTwo" : "–")}");
    }

    public void Dispose() => dispatcher.Dispose();

    // ── Public API ─────────────────────────────────────────────────────────────
    public Task<bool> SendToAsync(ChatRoute route, string? text, CancellationToken token = default, bool addPrefix = true)
    {
        var (aliases, chunkSize, delay, _, _, enabled, aiPrefix, r) = GetRouteParams(route);
        if (!enabled || string.IsNullOrWhiteSpace(text)) return Task.FromResult(false);

        var lines = PrepareForNetwork(text!, addPrefix ? aiPrefix : string.Empty, chunkSize);
        return SendLinesAsync(aliases, lines, r, delay, token);
    }

    public async Task<bool> SendStreamingToAsync(ChatRoute route, IAsyncEnumerable<string> tokens, string headerForFirstLine, CancellationToken token = default)
    {
        var (aliases, chunkSize, delay, flushCharsCfg, flushMsCfg, enabled, aiPrefix, r) = GetRouteParams(route);
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
                    await SendLineAsync(aliases, line, r, token);
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
            await SendLineAsync(aliases, line, r, token);
            first = false;
            finalCap = contCap;
            await Task.Delay(80, token);
        }
        return true;
    }

    // ── Core send ──────────────────────────────────────────────────────────────
    private async Task<bool> SendLinesAsync(string[] aliases, IEnumerable<string> lines, ChatRoute route, int delay, CancellationToken token)
    {
        bool any = false;
        foreach (var line in lines)
        {
            token.ThrowIfCancellationRequested();
            await SendLineAsync(aliases, line, route, token);
            await Task.Delay(delay, token);
            any = true;
        }
        return any;
    }

    private Task SendLineAsync(string[] aliases, string line, ChatRoute route, CancellationToken token)
    {
        var text = San(line);

        var tcs = new TaskCompletionSource();
        dispatcher.Enqueue(() =>
        {
            try
            {
                var sent = false;

                // 0) IPC bridge path
                if (!sent && bridge is { IsAvailable: true })
                {
                    sent = route == ChatRoute.Say
                        ? bridge.TrySendSay(text, config.AiDisplayName)
                        : bridge.TrySendParty(text, config.AiDisplayName);
                    if (sent) log.Info("ChatPipe → ChatTwoBridge (IPC)");
                }

                // 1) Prefer ChatGui direct sending
                if (!sent && config.PreferChatGuiSend)
                    sent = TryChatSendString((aliases.FirstOrDefault() ?? "/say ") + text) || TryChatSendTyped(aliases, text);

                // 2) Fallback to ICommandManager
                for (int i = 0; !sent && i < aliases.Length; i++)
                    sent = TryCommandDispatch(aliases[i] + text);

                // 3) If ChatGui not preferred, try it now
                if (!sent && !config.PreferChatGuiSend)
                    sent = TryChatSendString((aliases.FirstOrDefault() ?? "/say ") + text) || TryChatSendTyped(aliases, text);

                // 4) Prefill input box
                if (!sent && config.FallbackOpenChatInput && chatOpenInput is not null)
                {
                    var alias = aliases.FirstOrDefault() ?? "/say ";
                    var full = (config.FallbackOpenChatAutoSlash ? alias : string.Empty) + text;
                    try
                    {
                        chatOpenInput.Invoke(chat, new object[] { full });
                        log.Info($"ChatPipe → ChatGui.{chatOpenInput.Name}(prefill)");
                        sent = true;
                    }
                    catch (TargetInvocationException tex) { log.Error(tex.InnerException ?? tex, $"ChatGui.{chatOpenInput.Name} threw"); }
                    catch (Exception ex) { log.Error(ex, $"ChatGui.{chatOpenInput.Name} failed"); }
                }

                if (!sent)
                    throw new InvalidOperationException("No available chat send path succeeded.");

                OnEcho(route, text);
                tcs.TrySetResult();
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });

        using (token.Register(() => tcs.TrySetCanceled(token)))
            return tcs.Task;
    }

    private void OnEcho(ChatRoute route, string text)
    {
        if (config.DebugChatTap)
            chat.Print($"[AI Companion:{(route == ChatRoute.Party ? "Party" : "Say")}] → {text}");
        log.Info($"ChatPipe({(route == ChatRoute.Party ? "Party" : "Say")}) sent {text.Length} chars.");
    }

    // ── Private helpers added to fix missing symbols ──────────────────────────
    private bool TryChatSendString(string full)
    {
        if (chatSendString is null) return false;
        try
        {
            chatSendString.Invoke(chat, new object[] { full });
            log.Info("ChatPipe → IChatGui.StringSender");
            return true;
        }
        catch (TargetInvocationException tex) { log.Error(tex.InnerException ?? tex, "IChatGui.StringSender threw"); }
        catch (Exception ex) { log.Error(ex, "IChatGui.StringSender failed"); }
        return false;
    }

    private bool TryChatSendTyped(string[] aliases, string text)
    {
        if (chatSendTyped is null) return false;
        // We don’t know the enum type, but the first param is an enum for chat channel.
        // We’ll rely on slash alias in the string path instead, so this is a no-op unless
        // the reflection target is a (channel, string) where channel can be omitted. Most builds won’t use this.
        try
        {
            // best effort: try without setting enum (some impls accept (object? null, string))
            chatSendTyped.Invoke(chat, new object?[] { null, text });
            log.Info("ChatPipe → IChatGui.TypedSender(null, text)");
            return true;
        }
        catch { /* ignore; most builds won’t support this */ }
        return false;
    }

    private bool TryCommandDispatch(string full)
    {
        try
        {
            if (cmProcess is not null)
            {
                cmProcess.Invoke(commands, new object[] { full });
                log.Info("ChatPipe → ICommandManager.ProcessCommand()");
                return true;
            }
            if (cmDispatch is not null)
            {
                // Dispatch signatures vary; try the simple one first.
                var pars = cmDispatch.GetParameters();
                if (pars.Length == 1)
                {
                    cmDispatch.Invoke(commands, new object[] { full });
                }
                else if (pars.Length == 2)
                {
                    cmDispatch.Invoke(commands, new object[] { full, true });
                }
                else
                {
                    cmDispatch.Invoke(commands, new object?[] { full, null, null });
                }
                log.Info("ChatPipe → ICommandManager.DispatchCommand()");
                return true;
            }
        }
        catch (TargetInvocationException tex) { log.Error(tex.InnerException ?? tex, "ICommandManager.* threw"); }
        catch (Exception ex) { log.Error(ex, "ICommandManager.* failed"); }
        return false;
    }

    // ── discovery utils ───────────────────────────────────────────────────────
    private static MethodInfo? GetMethod(Type t, string name, Type[] sig) =>
        t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, sig, null);

    private static MethodInfo? FindStringSender(Type chatType)
    {
        var methods = chatType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return methods.FirstOrDefault(m =>
        {
            var ps = m.GetParameters();
            if (ps.Length != 1 || ps[0].ParameterType != typeof(string)) return false;
            var n = m.Name.ToLowerInvariant();
            return n.Contains("send") && (n.Contains("message") || n.Contains("chat") || n.Contains("string"));
        });
    }

    private static MethodInfo? FindTypedSender(Type chatType)
    {
        var methods = chatType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return methods.FirstOrDefault(m =>
        {
            var ps = m.GetParameters();
            if (ps.Length != 2) return false;
            if (!ps[0].ParameterType.IsEnum) return false;
            if (ps[1].ParameterType != typeof(string)) return false;
            var n = m.Name.ToLowerInvariant();
            return n.Contains("send") && (n.Contains("message") || n.Contains("chat"));
        });
    }

    private static MethodInfo? FindOpenChat(Type chatType)
    {
        var methods = chatType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return methods.FirstOrDefault(m =>
        {
            var ps = m.GetParameters();
            if (ps.Length != 1 || ps[0].ParameterType != typeof(string)) return false;
            var n = m.Name.ToLowerInvariant();
            return n.Contains("openchat") || n.Contains("withentry") || n.Contains("withmessage") || n.Contains("pastetext");
        });
    }

    private (string[] aliases, int chunk, int delay, int flushChars, int flushMs, bool enabled, string aiPrefix, ChatRoute route) GetRouteParams(ChatRoute route)
    {
        var aiName = string.IsNullOrWhiteSpace(config.AiDisplayName) ? "AI Nunu" : config.AiDisplayName;
        var prefix = config.UseAsciiHeaders ? $"[{aiName}] " : $"[{aiName}] ";
        return route switch
        {
            ChatRoute.Party => (new[] { "/party ", "/p " },
                                Math.Max(220, config.PartyChunkSize), Math.max(300, config.PartyPostDelayMs),
                                Math.Max(200, config.PartyStreamFlushChars), Math.Max(700, config.PartyStreamMinFlushMs),
                                config.EnablePartyPipe, prefix, route),
            ChatRoute.Say => (new[] { "/say ", "/s " },
                              Math.Max(220, config.SayChunkSize), Math.Max(300, config.SayPostDelayMs),
                              Math.Max(200, config.SayStreamFlushChars), Math.Max(700, config.SayStreamMinFlushMs),
                              config.EnableSayPipe, prefix, route),
            _ => (new[] { "/say " }, 280, 300, 200, 700, false, prefix, route),
        };
    }

    private static bool EndsWithSentence(StringBuilder sb)
    {
        int i = sb.Length - 1; while (i >= 0 && char.IsWhiteSpace(sb[i])) i--;
        if (i < 0) return false;
        char c = sb[i];
        if (c is '.' or '!' or '?' or '…') return true;
        if (c is '"' or '\'' or ')' or ']' or '»')
        {
            i--; if (i >= 0) { c = sb[i]; if (c is '.' or '!' or '?' or '…') return true; }
        }
        return false;
    }

    private static string TakeUntil(StringBuilder sb, int cap)
    {
        if (sb.Length <= cap) { var all = sb.ToString(); sb.Clear(); return all; }
        int cut = cap;
        for (int i = cap; i >= Math.Max(0, cap - 40); i--)
            if (i < sb.Length && char.IsWhiteSpace(sb[i])) { cut = i; break; }
        var s = sb.ToString(0, cut).TrimEnd();
        sb.Remove(0, cut);
        return s;
    }

    private IEnumerable<string> PrepareForNetwork(string text, string prefix, int chunkSize)
    {
        var s = (text ?? string.Empty).Replace("\r\n", " ").Replace("\n", " ");
        s = s.Replace("[", "［").Replace("]", "］").Replace("{", "(").Replace("}", ")");
        s = System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ").Trim();
        if (config.NetworkAsciiOnly)
            s = StripNonAscii(s);

        var max = Math.Max(180, chunkSize - prefix.Length);
        foreach (var chunk in ChunkSmart(s, max))
            yield return prefix + chunk;
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
            sb.Append(ch is >= (char)32 and <= (char)126 ? ch : ' ');
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "\\s+", " ").Trim();
    }

    private static string San(string s)
    {
        if (s == null) return string.Empty;
        s = s.Replace("\r\n", " ").Replace("\n", " ");
        return System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ").Trim();
    }
}
