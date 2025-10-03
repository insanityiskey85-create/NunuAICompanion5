using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

/// <summary>
/// SAY-only listener. On trigger from a whitelisted caller:
/// 1) Optional echo of the caller prompt to /s.
/// 2) Stream AI reply to /s as it generates.
/// </summary>
public sealed class SayListener : IDisposable
{
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly AiClient client;
    private readonly ChatPipe pipe;
    private static readonly char[] separator = new[] { ' ' };

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    public SayListener(IChatGui chat, IPluginLog log, Configuration config, AiClient client, ChatPipe pipe)
    {
        this.chat = chat;
        this.log = log;
        this.config = config;
        this.client = client;
        this.pipe = pipe;

        this.chat.ChatMessage += OnChatMessage;
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        throw new NotImplementedException();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    private void OnChatMessage(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        try
        {
            if (!config.EnableSayListener) return;
            if (type != XivChatType.Say) return;

            var senderName = NormalizeName(sender.TextValue);
            var msg = (message.TextValue ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(msg)) return;

            var trigger = config.SayTrigger ?? "!AI Nunu";
            if (string.IsNullOrWhiteSpace(trigger)) return;
            if (!msg.StartsWith(trigger, StringComparison.OrdinalIgnoreCase)) return;

            var allowed = (config.SayWhitelist?.Any() ?? false) &&
                          config.SayWhitelist.Any(w =>
                              string.Equals(NormalizeName(w), senderName, StringComparison.OrdinalIgnoreCase));
            if (!allowed)
            {
                log.Info($"SayListener: blocked non-whitelisted sender '{sender.TextValue}'.");
                return;
            }

            var prompt = msg[trigger.Length..].TrimStart(':', ' ', '-', '—').Trim();
            if (string.IsNullOrWhiteSpace(prompt)) return;
            if (!config.SayAutoReply) return;

            _ = RespondStreamAsync(prompt, senderName, GetLog());
        }
        catch (Exception ex)
        {
            log.Error(ex, "SayListener failed on chat event.");
        }
    }

    private IPluginLog GetLog() => log;

    private async Task RespondStreamAsync(string prompt, string caller, IPluginLog log)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

            // 1) Echo caller’s prompt (optional)
            if (config.SayEchoCallerPrompt)
            {
                var aiName = string.IsNullOrWhiteSpace(config.AiDisplayName) ? "AI Nunu" : config.AiDisplayName;
                var echo = (config.SayCallerEchoFormat ?? "{caller} -> {ai}: {prompt}")
                    .Replace("{caller}", caller)
                    .Replace("{ai}", aiName)
                    .Replace("{prompt}", prompt);
                await pipe.SendToAsync(ChatRoute.Say, echo, cts.Token, addPrefix: false).ConfigureAwait(false);
            }

            // 2) Build reply header
            var ai = string.IsNullOrWhiteSpace(config.AiDisplayName) ? "AI Nunu" : config.AiDisplayName;
            var format = config.SayAiReplyFormat ?? "{ai} -> {caller}: {reply}";
            var header = format.Replace("{ai}", ai).Replace("{caller}", caller).Replace("{reply}", string.Empty);

            // 3) Stream tokens to /s
            var tokens = client.ChatStreamAsync([], $"Caller: {caller}\n\n{prompt}", cts.Token);
            await pipe.SendStreamingToAsync(ChatRoute.Say, tokens, header, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
#pragma warning disable CA1416 // Validate platform compatibility
            log.Error(ex, "SayListener RespondStreamAsync error");
#pragma warning restore CA1416 // Validate platform compatibility
        }
    }

    private static string NormalizeName(string name)
    {
        var n = name ?? string.Empty;
        var at = n.IndexOf('@');
        if (at >= 0) n = n[..at];
        return string.Join(' ', n.Split(separator, StringSplitOptions.RemoveEmptyEntries));
    }

#pragma warning disable CA1416 // Validate platform compatibility
    public void Dispose() => chat.ChatMessage -= OnChatMessage;
#pragma warning restore CA1416 // Validate platform compatibility
}
