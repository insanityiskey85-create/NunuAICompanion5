// File: ChatTwoBridge.cs
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

/// <summary>
/// Optional bridge to external chat plugins. This stub compiles on all setups
/// and simply reports unavailable. You can later replace with real IPC calls.
/// </summary>
public sealed class ChatTwoBridge
{
    private readonly IDalamudPluginInterface pi;
    private readonly IPluginLog log;

    public ChatTwoBridge(IDalamudPluginInterface pi, IPluginLog log)
    {
        this.pi = pi;
        this.log = log;
    }

    public bool IsAvailable => false;

    public bool TrySendSay(string text)
    {
        // Not available; caller should fall back to /say command
        log.Debug("[ChatTwo] Not available; fallback to /say command.");
        return false;
    }

    public bool TrySendParty(string text)
    {
        log.Debug("[ChatTwo] Not available; fallback to /p command.");
        return false;
    }
}
