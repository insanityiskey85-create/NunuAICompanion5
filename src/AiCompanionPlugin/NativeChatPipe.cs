// NativeChatPipe.cs
#nullable enable
using System;

namespace AiCompanionPlugin;

internal static class NativeChatPipe
{
    private static long _lastSendMs;
    private const int MinIntervalMs = 250; // tiny throttle

    /// <summary>Post to /say (fallback uses ICommandManager).</summary>
    public static bool TrySay(string text)
        => TryExecute($"/say {text}");

    /// <summary>Post to /p (fallback uses ICommandManager).</summary>
    public static bool TryParty(string text)
        => TryExecute($"/p {text}");

    public static void SetChannelSay() { /* no-op in fallback */ }
    public static void SetChannelParty() { /* no-op in fallback */ }

    private static bool TryExecute(string rawLine)
    {
        try
        {
            var now = Environment.TickCount64;
            if (now - _lastSendMs < MinIntervalMs)
                return false;

            _lastSendMs = now;

            // Fallback path: use Dalamud's command manager to process a chat command.
            // This avoids FFXIVClientStructs entirely and will compile everywhere.
            Plugin.CommandManager.ProcessCommand(rawLine);
            Plugin.PluginLog.Info("[NativeChatPipe:Fallback] ProcessCommand -> \"{0}\"", rawLine);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.PluginLog.Error(ex, "[NativeChatPipe:Fallback] Failed ProcessCommand");
            return false;
        }
    }
}
