using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace AiCompanionPlugin;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 5;

    // ── Backend ────────────────────────────────────────────────────────────────
    public string BackendBaseUrl { get; set; } = "http://127.0.0.1:3001";
    public string ApiKey { get; set; } = string.Empty;          // if your proxy needs one
    public string Model { get; set; } = "qwen2.5:1.5b-instruct-q4_K_M";

    // Resolved endpoint (diagnostics)
    public string ResolvedEndpoint { get; set; } = string.Empty;

    // ASCII/Network safety
    public bool NetworkAsciiOnly { get; set; } = false;
    public bool UseAsciiHeaders { get; set; } = false;

    // Prefer direct ChatGui sends before commands
    public bool PreferChatGuiSend { get; set; } = true;

    // Fallback: open chat input pre-filled if no other route works
    public bool FallbackOpenChatInput { get; set; } = true;
    public bool FallbackOpenChatAutoSlash { get; set; } = true;

    // ── Persona & UI ───────────────────────────────────────────────────────────
    public string AiDisplayName { get; set; } = "AI Nunu";
    public string PersonaFileRelative { get; set; } = "persona.txt";
    public string SystemPromptOverride { get; set; } = string.Empty;

    // Theme selection
    public string ThemeName { get; set; } = "Void Touched";

    // Chat Window behaviour
    public int MaxHistoryMessages { get; set; } = 20;
    public bool StreamResponses { get; set; } = true;

    // ── Memory / Chronicle ─────────────────────────────────────────────────────
    public bool EnableMemory { get; set; } = true;
    public string MemoriesFileRelative { get; set; } = "memories.json";
    public bool AutoSaveMemory { get; set; } = true;
    public int MaxMemories { get; set; } = 256;

    public bool EnableChronicle { get; set; } = false;
    public string ChronicleFileRelative { get; set; } = "chronicle.json";
    public string ChronicleStyle { get; set; } = "tavern";
    public int ChronicleMaxEntries { get; set; } = 200;
    public bool ChronicleAutoAppend { get; set; } = true;

    // ── Channel Bridges (Say / Party) ─────────────────────────────────────────
    public bool EnablePartyListener { get; set; } = true;
    public bool EnableSayListener { get; set; } = true;

    public bool EnablePartyPipe { get; set; } = true;
    public bool EnableSayPipe { get; set; } = true;

    public string PartyTrigger { get; set; } = "!AI Nunu";
    public string SayTrigger { get; set; } = "!AI Nunu";

    // Whitelisting
    public bool RequireWhitelist { get; set; } = true;
    public List<string> PartyWhitelist { get; set; } = new();
    public List<string> SayWhitelist { get; set; } = new();

    // Echo formats (shown in debug only)
    public string PartyCallerEchoFormat { get; set; } = "[AI Companion:Party] → {caller}: {text}";
    public string PartyAiReplyFormat { get; set; } = "[AI Companion:Party] → [{name}] {text}";
    public string PartyEchoCallerPrompt { get; set; } = "Party @{caller}: {text}";

    public string SayCallerEchoFormat { get; set; } = "[AI Companion:Say] → {caller}: {text}";
    public string SayAiReplyFormat { get; set; } = "[AI Companion:Say] → [{name}] {text}";
    public string SayEchoCallerPrompt { get; set; } = "Say @{caller}: {text}";

    // Stream chunking / pacing
    public int SayChunkSize { get; set; } = 280;
    public int SayPostDelayMs { get; set; } = 400;
    public int SayStreamFlushChars { get; set; } = 220;
    public int SayStreamMinFlushMs { get; set; } = 900;

    public int PartyChunkSize { get; set; } = 280;
    public int PartyPostDelayMs { get; set; } = 400;
    public int PartyStreamFlushChars { get; set; } = 220;
    public int PartyStreamMinFlushMs { get; set; } = 900;

    // Debug
    public bool DebugChatTap { get; set; } = false;
    public int DebugChatTapLimit { get; set; } = 400;

    // Non-serialized runtime
    [NonSerialized] private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;
    public void Save() => pluginInterface?.SavePluginConfig(this);

    public string GetPersonaAbsolutePath(IDalamudPluginInterface pi)
    {
        var dir = pi.GetPluginConfigDirectory();
        System.IO.Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, PersonaFileRelative);
    }

    public string GetMemoriesAbsolutePath(IDalamudPluginInterface pi)
    {
        var dir = pi.GetPluginConfigDirectory();
        System.IO.Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, MemoriesFileRelative);
    }

    public string GetChronicleAbsolutePath(IDalamudPluginInterface pi)
    {
        var dir = pi.GetPluginConfigDirectory();
        System.IO.Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, ChronicleFileRelative);
    }
}
