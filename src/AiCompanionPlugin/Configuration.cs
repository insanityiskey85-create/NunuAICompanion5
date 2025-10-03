using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace AiCompanionPlugin;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 10;

    // Backend
    public string BackendBaseUrl { get; set; } = "http://127.0.0.1:11434";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "llama3.1";

    // Persona
    public string PersonaFileRelative { get; set; } = "persona.txt";
    public string SystemPromptOverride { get; set; } = string.Empty;

    // Chat UX
    public int MaxHistoryMessages { get; set; } = 20;
    public bool StreamResponses { get; set; } = true;

    // Theme
    public string ThemeName { get; set; } = "Eorzean Night";

    // Memory
    public bool EnableMemory { get; set; } = true;
    public bool AutoSaveMemory { get; set; } = true;
    public int MaxMemories { get; set; } = 256;
    public string MemoriesFileRelative { get; set; } = "memories.json";

    // Display
    public string AiDisplayName { get; set; } = "AI Nunu";

    // Chronicle
    public bool EnableChronicle { get; set; } = true;
    public bool ChronicleAutoAppend { get; set; } = true;
    public int ChronicleMaxEntries { get; set; } = 1000;
    public string ChronicleFileRelative { get; set; } = "chronicle.json";
    public string ChronicleStyle { get; set; } = "Canon";

    // PARTY — Pipe & Listener
    public bool EnablePartyPipe { get; set; } = false;
    public bool ConfirmBeforePartyPost { get; set; } = true;
    public int PartyChunkSize { get; set; } = 440;
    public int PartyPostDelayMs { get; set; } = 800;

    public bool EnablePartyListener { get; set; } = false;
    public string PartyTrigger { get; set; } = "!AI Nunu";
    public List<string> PartyWhitelist { get; set; } = new() { "Your Name Here" };
    public bool PartyAutoReply { get; set; } = true;

    // SAY — Pipe & Listener
    public bool EnableSayPipe { get; set; } = false;
    public int SayChunkSize { get; set; } = 440;
    public int SayPostDelayMs { get; set; } = 800;

    public bool EnableSayListener { get; set; } = false;
    public string SayTrigger { get; set; } = "!AI Nunu";
    public List<string> SayWhitelist { get; set; } = new() { "Your Name Here" };
    public bool SayAutoReply { get; set; } = true;

    // Formatting
    public bool PartyEchoCallerPrompt { get; set; } = true;
    public string PartyCallerEchoFormat { get; set; } = "{caller} \u2192 {ai}: {prompt}";
    public string PartyAiReplyFormat { get; set; } = "{ai} \u2192 {caller}: {reply}";

    public bool SayEchoCallerPrompt { get; set; } = true;
    public string SayCallerEchoFormat { get; set; } = "{caller} \u2192 {ai}: {prompt}";
    public string SayAiReplyFormat { get; set; } = "{ai} \u2192 {caller}: {reply}";

    // Streaming thresholds
    public int PartyStreamFlushChars { get; set; } = 180;
    public int PartyStreamMinFlushMs { get; set; } = 600;
    public int SayStreamFlushChars { get; set; } = 180;
    public int SayStreamMinFlushMs { get; set; } = 600;

    // add near the other settings
    public bool NetworkAsciiOnly { get; set; } = true;   // strip emojis/non-ASCII before network send
    public bool UseAsciiHeaders { get; set; } = true;   // use "->" instead of "→" in channel headers

    // NEW: Debug/Troubleshooting
    public bool DebugChatTap { get; set; } = true;       // log incoming chat kinds & text
    public int DebugChatTapLimit { get; set; } = 100;    // max entries per session
    public bool RequireWhitelist { get; set; } = true;   // if false, empty whitelist allows everyone (for testing)

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
        return System.IO.Path.Combine(dir, string.IsNullOrWhiteSpace(MemoriesFileRelative) ? "memories.json" : MemoriesFileRelative);
    }
    public string GetChronicleAbsolutePath(IDalamudPluginInterface pi)
    {
        var dir = pi.GetPluginConfigDirectory();
        System.IO.Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, string.IsNullOrWhiteSpace(ChronicleFileRelative) ? "chronicle.json" : ChronicleFileRelative);
    }
}
