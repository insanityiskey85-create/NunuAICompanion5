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
/// PARTY-only listener. On trigger from a whitelisted caller:
/// 1) Optional echo of the caller prompt.
/// 2) Stream AI reply to /p as it generates.
/// </summary>
public sealed class PartyListener : IDisposable
{
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly AiClient client;
    private readonly ChatPipe pipe;

    public PartyListener(IChatGui chat, IPluginLog log, Configuration config, AiClient client, ChatPipe pipe)
    {
        this.chat = chat;
        this.log = log;
        this.config = config;
        this.client = client;
        this.pipe = pipe;

        this.chat.ChatMessage += OnChatMessage;
    }

    private void OnChatMessage(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        try
        {
            if (!config.EnablePartyListener) return;
            if (type != XivChatType.Party) return;

            var senderName = NormalizeName(sender.TextValue);
            var msg = (message.TextValue ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(msg)) return;

            var trigger = config.PartyTrigger ?? "!AI Nunu";
            if (string.IsNullOrWhiteSpace(trigger)) return;
            if (!msg.StartsWith(trigger, StringComparison.OrdinalIgnoreCase)) return;

            var allowed = (config.PartyWhitelist?.Any() ?? false) &&
                          config.PartyWhitelist.Any(w =>
                              string.Equals(NormalizeName(w), senderName, StringComparison.OrdinalIgnoreCase));
            if (!allowed)
            {
                log.Info($"PartyListener: blocked non-whitelisted sender '{sender.TextValue}'.");
                return;
            }

            var prompt = msg.Substring(trigger.Length).TrimStart(':', ' ', '-', '—').Trim();
            if (string.IsNullOrWhiteSpace(prompt)) return;
            if (!config.PartyAutoReply) return;

            _ = RespondStreamAsync(prompt, senderName);
        }
        catch (Exception ex)
        {
            log.Error(ex, "PartyListener failed on chat event.");
        }
    }

    private async Task RespondStreamAsync(string prompt, string caller)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

            // 1) Echo caller’s prompt (optional), not prefixed
            if (config.PartyEchoCallerPrompt)
            {
                var echo = (config.PartyCallerEchoFormat ?? "{caller} -> {ai}: {prompt}")
                    .Replace("{caller}", caller)
                    .Replace("{ai}", string.IsNullOrWhiteSpace(config.AiDisplayName) ? "AI Nunu" : config.AiDisplayName)
                    .Replace("{prompt}", prompt);
                await pipe.SendToPartyAsync(echo, cts.Token, addPrefix: false).ConfigureAwait(false);
            }

            // 2) Build header (e.g., "AI Nunu → Caller: ")
            var aiName = string.IsNullOrWhiteSpace(config.AiDisplayName) ? "AI Nunu" : config.AiDisplayName;
            var format = config.PartyAiReplyFormat ?? "{ai} -> {caller}: {reply}";
            var header = format
                .Replace("{ai}", aiName)
                .Replace("{caller}", caller)
                .Replace("{reply}", string.Empty); // streaming will fill reply live

            // 3) Start token stream
            var tokens = client.ChatStreamAsync(new List<ChatMessage>(), // no history for quick party replies
                $"Caller: {caller}\n\n{prompt}",
                cts.Token);

            await pipe.SendStreamingToPartyAsync(tokens, header, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            log.Error(ex, "PartyListener RespondStreamAsync error");
        }
    }

    private static string NormalizeName(string name)
    {
        var n = name ?? string.Empty;
        var at = n.IndexOf('@');
        if (at >= 0) n = n.Substring(0, at);
        return string.Join(' ', n.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
    }

    public void Dispose()
    {
        chat.ChatMessage -= OnChatMessage;
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        throw new NotImplementedException();
    }
}
