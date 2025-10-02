using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

/// <summary>
/// Listens to PARTY chat. When a message begins with the trigger (e.g., "!AI Nunu"),
/// and the sender is on the whitelist, sends the remainder to AI, then posts the reply to party via ChatPipe.
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

        chat.ChatMessage += OnChatMessage;
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

            // Raw values
            var senderName = NormalizeName(sender.TextValue); // "Name Lastname" or "Name Lastname@World"
            var msg = (message?.TextValue ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(msg)) return;

            // Trigger match at start (case-insensitive)
            var trigger = config.PartyTrigger ?? "!AI Nunu";
            if (string.IsNullOrWhiteSpace(trigger)) return;
            if (!msg.StartsWith(trigger, StringComparison.OrdinalIgnoreCase)) return;

            // Whitelist check (name only; ignore @World if present)
            var allowed = config.PartyWhitelist?.Any() == true
                && config.PartyWhitelist.Any(w => string.Equals(NormalizeName(w), senderName, StringComparison.OrdinalIgnoreCase));

            if (!allowed)
            {
                log.Info($"PartyListener: blocked non-whitelisted sender '{sender.TextValue}'.");
                return;
            }

            // Extract prompt after trigger text
            var prompt = msg.Substring(trigger.Length).TrimStart(':', ' ', '-', '—').Trim();
            if (string.IsNullOrWhiteSpace(prompt))
                return;

            if (!config.PartyAutoReply)
                return;

            _ = RespondAsync(prompt);
        }
        catch (Exception ex)
        {
            log.Error(ex, "PartyListener failed on chat event.");
        }
    }

    private async Task RespondAsync(string prompt)
    {
        try
        {
            // No history: keep simple and fast for party replies
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var reply = await client.ChatOnceAsync(new System.Collections.Generic.List<ChatMessage>(), prompt, cts.Token).ConfigureAwait(false);
            reply = (reply ?? "").Trim();
            if (reply.Length == 0) return;

            await pipe.SendToPartyAsync(reply, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // quiet
        }
        catch (Exception ex)
        {
            log.Error(ex, "PartyListener RespondAsync error");
        }
    }

    private static string NormalizeName(string name)
    {
        // Strip @World suffix and condense spaces
        var n = name ?? "";
        var at = n.IndexOf('@');
        if (at >= 0) n = n.Substring(0, at);
        return string.Join(' ', n.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
    }

    public void Dispose()
    {
        chat.ChatMessage -= OnChatMessage;
    }
}
