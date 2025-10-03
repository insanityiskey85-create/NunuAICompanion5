using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

public sealed class PartyListener : IDisposable
{
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly AiClient client;
    private readonly ChatPipe pipe;
    private static readonly char[] separator = new[] { ' ' };

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    private async Task RespondStreamAsync(string prompt, string caller)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

            if (config.PartyEchoCallerPrompt)
            {
                var aiName = string.IsNullOrWhiteSpace(config.AiDisplayName) ? "AI Nunu" : config.AiDisplayName;
                var echo = (config.PartyCallerEchoFormat ?? "{caller} -> {ai}: {prompt}")
                    .Replace("{caller}", caller).Replace("{ai}", aiName).Replace("{prompt}", prompt);
                await pipe.SendToAsync(ChatRoute.Party, echo, cts.Token, addPrefix: false).ConfigureAwait(false);
            }

            var ai = string.IsNullOrWhiteSpace(config.AiDisplayName) ? "AI Nunu" : config.AiDisplayName;
            var format = config.PartyAiReplyFormat ?? "{ai} -> {caller}: {reply}";
            var header = format.Replace("{ai}", ai).Replace("{caller}", caller).Replace("{reply}", string.Empty);

            var tokens = client.ChatStreamAsync([], $"Caller: {caller}\n\n{prompt}", cts.Token);
            await pipe.SendStreamingToAsync(ChatRoute.Party, tokens, header, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { log.Error(ex, "PartyListener RespondStreamAsync error"); }
    }

    private static string NormalizeName(string name)
    {
        var n = name ?? string.Empty;
        var at = n.IndexOf('@'); if (at >= 0) n = n[..at];
        return string.Join(' ', n.Split(separator, StringSplitOptions.RemoveEmptyEntries));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    public void Dispose() => chat.ChatMessage -= OnChatMessage;
}
