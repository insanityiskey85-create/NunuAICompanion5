using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;            // <-- IPC types live here
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

/// <summary>
/// Optional bridge to the ChatTwo plugin via IPC. All calls are best-effort.
/// </summary>
public sealed class ChatTwoBridge
{
    private readonly IDalamudPluginInterface pi;
    private readonly IPluginLog log;

    private IIpcSubscriber<string, string, bool>? say3;   // TrySendSay(displayName, text)
    private IIpcSubscriber<string, bool>? say2;   // TrySendSay(text)
    private IIpcSubscriber<string, bool>? say1;   // SendSay(text)

    private IIpcSubscriber<string, string, bool>? party3; // TrySendParty(displayName, text)
    private IIpcSubscriber<string, bool>? party2; // TrySendParty(text)
    private IIpcSubscriber<string, bool>? party1; // SendParty(text)

    public bool IsAvailable { get; private set; }

    public ChatTwoBridge(IDalamudPluginInterface pi, IPluginLog log)
    {
        this.pi = pi;
        this.log = log;

        // Resolve (different installs expose different keys; we probe many)
        say3 = SafeGet<string, string, bool>("ChatTwo.TrySendSay3");
        say2 = SafeGet<string, bool>("ChatTwo.TrySendSay");
        say1 = SafeGet<string, bool>("ChatTwo.SendSay");

        party3 = SafeGet<string, string, bool>("ChatTwo.TrySendParty3");
        party2 = SafeGet<string, bool>("ChatTwo.TrySendParty");
        party1 = SafeGet<string, bool>("ChatTwo.SendParty");

        IsAvailable = say3 is not null || say2 is not null || say1 is not null
                   || party3 is not null || party2 is not null || party1 is not null;
    }

    private IIpcSubscriber<T1, T2, T3>? SafeGet<T1, T2, T3>(string name)
    {
        try { return pi.GetIpcSubscriber<T1, T2, T3>(name); }
        catch { return null; }
    }

    private IIpcSubscriber<T1, T2>? SafeGet<T1, T2>(string name)
    {
        try { return pi.GetIpcSubscriber<T1, T2>(name); }
        catch { return null; }
    }

    public bool TrySendSay(string text, string? displayName = null)
    {
        try
        {
            if (displayName is not null && say3 is not null)
                return say3.InvokeFunc(displayName, text);
            if (say2 is not null) return say2.InvokeFunc(text);
            if (say1 is not null) return say1.InvokeFunc(text);
        }
        catch (Exception ex) { log.Error(ex, "[ChatTwoBridge] TrySendSay failed"); }
        return false;
    }

    public bool TrySendParty(string text, string? displayName = null)
    {
        try
        {
            if (displayName is not null && party3 is not null)
                return party3.InvokeFunc(displayName, text);
            if (party2 is not null) return party2.InvokeFunc(text);
            if (party1 is not null) return party1.InvokeFunc(text);
        }
        catch (Exception ex) { log.Error(ex, "[ChatTwoBridge] TrySendParty failed"); }
        return false;
    }
}
