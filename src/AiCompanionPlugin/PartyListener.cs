using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

public sealed class PartyListener : IDisposable
{
    private readonly IPluginLog log;
    private readonly IChatGui chat;
    private readonly Configuration config;
    private readonly AiClient client;
    private readonly ChatPipe pipe;

    public PartyListener(IPluginLog log, IChatGui chat, Configuration config, AiClient client, ChatPipe pipe)
    {
        this.log = log;
        this.chat = chat;
        this.config = config;
        this.client = client;
        this.pipe = pipe;

        chat.CheckMessageHandled += OnChatMessage;
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (type != XivChatType.Party || !config.EnablePartyListener) return;

        var t = config.PartyTrigger?.Trim();
        var text = message.TextValue ?? string.Empty;
        var name = sender.TextValue ?? string.Empty;

        if (string.IsNullOrWhiteSpace(t) || !text.TrimStart().StartsWith(t, StringComparison.OrdinalIgnoreCase))
            return;

        if (config.RequireWhitelist && !config.PartyWhitelist.Contains(name))
            return;

        var prompt = text.Trim().Substring(t.Length).Trim();
        if (string.IsNullOrEmpty(prompt)) return;

        // Echo line (debug only)
        if (config.DebugChatTap)
        {
            var echo = config.PartyCallerEchoFormat
                .Replace("{caller}", name)
                .Replace("{text}", prompt);
            chat.Print(echo);
        }

        // Stream reply back out to Party
        _ = Task.Run(async () =>
        {
            try
            {
                // header for first line—optional
                var header = $"AI Nunu → {name}: ";
                await pipe.SendStreamingToAsync(ChatRoute.Party,
                    client.ChatStreamAsync(new System.Collections.Generic.List<ChatMessage>(), prompt, CancellationToken.None),
                    header,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                log.Error(ex, "[PartyListener] stream failed");
            }
        });
    }

    public void Dispose()
    {
        chat.CheckMessageHandled -= OnChatMessage;
    }
}
