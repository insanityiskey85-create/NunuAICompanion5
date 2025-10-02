using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

/// <summary>
/// Listens to PARTY only. On trigger by whitelisted caller:
/// 1) (optional) Echoes caller's prompt to /p using the caller's name.
/// 2) Queries AI and replies with formatted line addressing the caller.
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

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        throw new NotImplementedException();
    }

    private void OnChatMessage(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        try
        {
            if (!config.EnablePartyListener) return;
            if (type != XivChatType.Party) return;

            var senderName = NormalizeName(sender.TextValue); // "First Last"
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

            _ = RespondAsync(prompt, senderName);
        }
        catch (Exception ex)
        {
            log.Error(ex, "PartyListener failed on chat event.");
        }
    }

    private async Task RespondAsync(string prompt, string caller)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

            // 1) Echo caller's prompt (optional), *without* AI prefix
            if (config.PartyEchoCallerPrompt)
            {
                var echo = (config.PartyCallerEchoFormat ?? "{caller} -> {ai}: {prompt}")
                    .Replace("{caller}", caller)
                    .Replace("{ai}", string.IsNullOrWhiteSpace(config.AiDisplayName) ? "AI Nunu" : config.AiDisplayName)
                    .Replace("{prompt}", prompt);
                await pipe.SendToPartyAsync(echo, cts.Token, addPrefix: false).ConfigureAwait(false);
            }

            // 2) Ask AI (quick single-shot)
            var addressedPrompt = $"Caller: {caller}\n\n{prompt}";
            var reply = await client.ChatOnceAsync(
                new System.Collections.Generic.List<ChatMessage>(),
                addressedPrompt,
                cts.Token).ConfigureAwait(false);

            reply = (reply ?? string.Empty).Trim();
            if (reply.Length == 0) return;

            // 3) Format reply (AI prefix kept by ChatPipe)
            var text = (config.PartyAiReplyFormat ?? "{ai} -> {caller}: {reply}")
                .Replace("{caller}", caller)
                .Replace("{ai}", string.IsNullOrWhiteSpace(config.AiDisplayName) ? "AI Nunu" : config.AiDisplayName)
                .Replace("{reply}", reply);

            await pipe.SendToPartyAsync(text, cts.Token, addPrefix: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            log.Error(ex, "PartyListener RespondAsync error");
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
}
