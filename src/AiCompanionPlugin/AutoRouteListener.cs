using System;
using System.Linq;
using Dalamud.Plugin.Services;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;

namespace AiCompanionPlugin;

public sealed class AutoRouteListener : IDisposable
{
    private readonly IPluginLog log;
    private readonly IChatGui chat;
    private readonly Configuration config;

    public AutoRouteListener(IPluginLog log, IChatGui chat, Configuration config)
    {
        this.log = log;
        this.chat = chat;
        this.config = config;
        this.chat.ChatMessage += OnChatMessage;
        log.Information("[AutoRoute] listener armed");
    }

    public void Dispose() => this.chat.ChatMessage -= OnChatMessage;

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (!(type == XivChatType.Say || type == XivChatType.Party))
            return;

        var text = message.TextValue?.Trim() ?? string.Empty;
        var name = sender.TextValue?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(name))
            return;

        var trigger = type == XivChatType.Say ? config.SayTrigger : config.PartyTrigger;
        if (string.IsNullOrWhiteSpace(trigger)) return;
        if (!text.StartsWith(trigger, StringComparison.OrdinalIgnoreCase)) return;

        if (config.RequireWhitelist)
        {
            var list = type == XivChatType.Say ? (config.SayWhitelist ?? Array.Empty<string>()) : (config.PartyWhitelist ?? Array.Empty<string>());
            if (!list.Any(w => string.Equals(w?.Trim(), name, StringComparison.Ordinal)))
                return;
        }

        Plugin.RouteIncoming(type, name, text.Substring(trigger.Length).Trim());
    }
}
