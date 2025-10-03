using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace AiCompanionPlugin;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // ===== Backend =====
    public string BackendBaseUrl { get; set; } = "http://127.0.0.1:3001"; // your local proxy to Ollama
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "qwen2.5:1.5b-instruct-q4_K_M";

    // ===== Persona =====
    public string PersonaFileRelative { get; set; } = "persona.txt";
    public string SystemPromptOverride { get; set; } = string.Empty; // hot-reloaded cache of persona.txt

    // ===== Theme / UI =====
    public string ThemeName { get; set; } = "Void Touched";
    public string AiDisplayName { get; set; } = "AI Nunu";

    // ===== Chat UX =====
    public int MaxHistoryMessages { get; set; } = 20;
    public bool StreamResponses { get; set; } = true;

    // ===== Routing (posting to game chat) =====
    public bool EnableSayPipe { get; set; } = true;
    public bool EnablePartyPipe { get; set; } = true;

    public int SayChunkSize { get; set; } = 300;
    public int SayPostDelayMs { get; set; } = 500;
    public int SayStreamFlushChars { get; set; } = 260;
    public int SayStreamMinFlushMs { get; set; } = 900;

    public int PartyChunkSize { get; set; } = 300;
    public int PartyPostDelayMs { get; set; } = 500;
    public int PartyStreamFlushChars { get; set; } = 260;
    public int PartyStreamMinFlushMs { get; set; } = 900;

    public bool NetworkAsciiOnly { get; set; } = true; // strip non-ASCII before posting to channels
    public bool UseAsciiHeaders { get; set; } = true;  // avoid fancy glyphs in headers/prefix
    public bool PreferChatGuiSend { get; set; } = true; // try ChatGui path before CommandManager

    // ===== Listeners (reading from game chat) =====
    public bool EnableSayListener { get; set; } = true;
    public bool EnablePartyListener { get; set; } = true;

    // Trigger phrases (exact, case-sensitive match used upstream)
    public string SayTrigger { get; set; } = "!AI Nunu";
    public string PartyTrigger { get; set; } = "!AI Nunu";

    // Whitelist requirements
    public bool RequireWhitelist { get; set; } = true;
    public List<string> SayWhitelist { get; set; } = new() { "Nunubu Nubu" };
    public List<string> PartyWhitelist { get; set; } = new() { "Nunubu Nubu" };

    // Auto-reply toggles
    public bool SayAutoReply { get; set; } = false;
    public bool PartyAutoReply { get; set; } = false;

    // Echo formats (for debug/preview text in Debug channel or logs)
    public bool SayEchoCallerPrompt { get; set; } = true;
    public bool PartyEchoCallerPrompt { get; set; } = true;

    // Caller echo format (use tokens: {Caller}, {AiName}, {Prompt})
    public string SayCallerEchoFormat { get; set; } = "[AI Companion:Say] → {Caller} → {AiName}: {Prompt}";
    public string PartyCallerEchoFormat { get; set; } = "[AI Companion:Party] → {Caller} → {AiName}: {Prompt}";

    // AI reply format (use tokens: {AiName}, {Caller}, {Reply})
    public string SayAiReplyFormat { get; set; } = "[{AiName}] {AiName} → {Caller}: {Reply}";
    public string PartyAiReplyFormat { get; set; } = "[{AiName}] {AiName} → {Caller}: {Reply}";

    // ===== Debugging =====
    public bool DebugChatTap { get; set; } = false; // mirror listener/pipe info to chat
    public int DebugChatTapLimit { get; set; } = 200; // cap printed lines per session

    // ===== Memory (memories.json) =====
    public bool EnableMemory { get; set; } = true;
    public bool AutoSaveMemory { get; set; } = true;
    public int MaxMemories { get; set; } = 2000;
    public string MemoriesFileRelative { get; set; } = "memories.json";

    // ===== Chronicle (optional running log) =====
    public bool EnableChronicle { get; set; } = false;
    public string ChronicleFileRelative { get; set; } = "chronicle.txt";
    public string ChronicleStyle { get; set; } = "Plain"; // or "Markdown"
    public int ChronicleMaxEntries { get; set; } = 5000;
    public bool ChronicleAutoAppend { get; set; } = false;

    // ===== infra =====
    [NonSerialized] private IDalamudPluginInterface? _pi;
    public void Initialize(IDalamudPluginInterface pi) => _pi = pi;
    public void Save() => _pi?.SavePluginConfig(this);

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
