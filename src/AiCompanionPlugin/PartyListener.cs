// File: PartyListener.cs
using Dalamud.Plugin.Services;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;

namespace AiCompanionPlugin;

public sealed class PartyListener : System.IDisposable
{
    private readonly IPluginLog log;
    private readonly IChatGui chat;
    private readonly Configuration config;

    public PartyListener(IPluginLog log, IChatGui chat, Configuration config)
    {
        this.log = log;
        this.chat = chat;
        this.config = config;
        chat.ChatMessage += OnChatMessage;
    }

    public PartyListener(IPluginLog log, IChatGui chat, Configuration config, AiClient aiClient, ChatPipe pipe) : this(log, chat, config)
    {
    }

    public void Dispose() => chat.ChatMessage -= OnChatMessage;

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (type != XivChatType.Party) return;
        // handled globally in AutoRouteListener now
    }
}
