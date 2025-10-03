using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace AiCompanionPlugin;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    // Bump this any time the schema changes
    public int Version { get; set; } = 11;

    // ===== Backend =====
    public string BackendBaseUrl { get; set; } = "http://127.0.0.1:11434";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "qwen2.5:1.5b-instruct-q4_K_M"; // or llama3.1 etc.
    public bool StreamResponses { get; set; } = true;
    public int MaxHistoryMessages { get; set; } = 20;

    // ===== Persona / Display =====
    public string PersonaFileRelative { get; set; } = "persona.txt";
    public string SystemPromptOverride { get; set; } = string.Empty; // cached text of persona.txt
    public string AiDisplayName { get; set; } = "AI Nunu";

    // ===== Theme (basic hook, actual palettes live elsewhere) =====
    public string ThemeName { get; set; } = "Void Touched";

    // ===== Memory / Chronicle =====
    public bool EnableMemory { get; set; } = true;
    public bool AutoSaveMemory { get; set; } = true;
    public int MaxMemories { get; set; } = 256;
    public string MemoriesFileRelative { get; set; } = "memories.json";

    public bool EnableChronicle { get; set; } = true;
    public bool ChronicleAutoAppend { get; set; } = true;
    public int ChronicleMaxEntries { get; set; } = 1000;
    public string ChronicleFileRelative { get; set; } = "chronicle.json";
    public string ChronicleStyle { get; set; } = "Canon";

    // ===== Chat Routing: Party (/party) =====
    // Send (pipe)
    public bool EnablePartyPipe { get; set; } = false;
    public int PartyChunkSize { get; set; } = 420;     // max characters per posted line (including prefixes)
    public int PartyPostDelayMs { get; set; } = 600;   // delay between posted lines
    public int PartyStreamFlushChars { get; set; } = 220; // streaming: flush when this many chars buffered
    public int PartyStreamMinFlushMs { get; set; } = 900; // streaming: minimal wait before flushing

    // Receive (listener)
    public bool EnablePartyListener { get; set; } = false;
    public string PartyTrigger { get; set; } = "!AI Nunu";
    public List<string> PartyWhitelist { get; set; } = new() { "Your Name Here" };
    public bool PartyAutoReply { get; set; } = true;

    // Formatting
    public bool PartyEchoCallerPrompt { get; set; } = true;
    public string PartyCallerEchoFormat { get; set; } = "{caller} -> {ai}: {prompt}";
    public string PartyAiReplyFormat { get; set; } = "{ai} -> {caller}: {reply}";

    // ===== Chat Routing: Say (/say) =====
    // Send (pipe)
    public bool EnableSayPipe { get; set; } = false;
    public int SayChunkSize { get; set; } = 420;
    public int SayPostDelayMs { get; set; } = 600;
    public int SayStreamFlushChars { get; set; } = 220;
    public int SayStreamMinFlushMs { get; set; } = 900;

    // Receive (listener)
    public bool EnableSayListener { get; set; } = false;
    public string SayTrigger { get; set; } = "!AI Nunu";
    public List<string> SayWhitelist { get; set; } = new() { "Your Name Here" };
    public bool SayAutoReply { get; set; } = true;

    // Formatting
    public bool SayEchoCallerPrompt { get; set; } = true;
    public string SayCallerEchoFormat { get; set; } = "{caller} -> {ai}: {prompt}";
    public string SayAiReplyFormat { get; set; } = "{ai} -> {caller}: {reply}";

    // ===== Debug & Safety =====
    public bool DebugChatTap { get; set; } = false;   // when true, log inbound/outbound debug to plugin log / chat
    public int DebugChatTapLimit { get; set; } = 200; // max entries per session
    public bool RequireWhitelist { get; set; } = true;

    // Network text safety
    public bool NetworkAsciiOnly { get; set; } = true; // strip emojis/non-ascii before sending
    public bool UseAsciiHeaders { get; set; } = true;  // "->" instead of "→" in headers

    // ===== Dalamud plumbing =====
    [NonSerialized] private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;

    public void Save() => pluginInterface?.SavePluginConfig(this);

    // ===== Helper paths (all relative to plugin config folder) =====
    public string GetPersonaAbsolutePath(IDalamudPluginInterface pi)
    {
        var dir = pi.GetPluginConfigDirectory();
        System.IO.Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, string.IsNullOrWhiteSpace(PersonaFileRelative) ? "persona.txt" : PersonaFileRelative);
    }

    public string GetMemoriesAbsolutePath(IDalamudPluginInterface pi)
    {
        var dir = pi.GetPluginConfigDirectory();
        System.IO.Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, string.IsNullOrWhiteSpace(MemoriesFileRelative) ? "memories.json" : MemoriesFileRelative);
    }

    public string GetChronicleAbsolutePath(IDalamudPluginInterface pi)
    {
        var dir = pi.GetPluginConfigDirectory();
        System.IO.Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, string.IsNullOrWhiteSpace(ChronicleFileRelative) ? "chronicle.json" : ChronicleFileRelative);
    }
}
