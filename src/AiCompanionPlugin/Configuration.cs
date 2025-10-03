using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.IO;
using System.Linq;

namespace AiCompanionPlugin;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    // ==== Core ====
    public int Version { get; set; } = 9;

    // Backend
    public string BackendBaseUrl { get; set; } = "http://127.0.0.1:3001";     // e.g., local proxy for Ollama/OpenAI-style
    public string ApiKey { get; set; } = "";                                   // optional
    public string Model { get; set; } = "qwen2.5:1.5b-instruct-q4_K_M";         // default model
    public bool StreamResponses { get; set; } = true;                           // SSE token stream

    // AI Identity / Persona
    public string AiDisplayName { get; set; } = "AI Nunu";
    public string PersonaFileRelative { get; set; } = "persona.txt";
    public string SystemPromptOverride { get; set; } = "";                      // cache of loaded persona (hot-reload by PersonaManager)
    public int MaxHistoryMessages { get; set; } = 20;

    // Theme
    public string ThemeName { get; set; } = "Void Touched";                     // preset name
    // Optional per-user RGBA overrides (0..1). If null, the theme preset is used.
    public float[]? AccentColor { get; set; } = null;                           // e.g., [0.62f,0.35f,0.84f,1f] (void purple)
    public float[]? BgColor { get; set; } = null;
    public float[]? PanelColor { get; set; } = null;
    public float[]? TextColor { get; set; } = null;

    // ==== Chat Listeners / Routing ====
    public bool EnablePartyListener { get; set; } = true;
    public bool EnableSayListener { get; set; } = true;

    // Triggers
    public string PartyTrigger { get; set; } = "!AI Nunu";
    public string SayTrigger { get; set; } = "!AI Nunu";

    // Whitelisting
    public bool RequireWhitelist { get; set; } = true;
    public string[] PartyWhitelist { get; set; } = new[] { "Nunubu Nubu" };     // exact, case-sensitive
    public string[] SayWhitelist { get; set; } = new[] { "Nunubu Nubu" };

    // Echo / Format (shown in debug pane or window transcript mirrors)
    public string PartyCallerEchoFormat { get; set; } = "[AI Companion:Party] → {Sender} → {AiName}: {Prompt}";
    public string PartyAiReplyFormat { get; set; } = "[AI Companion:Party] → [{AiName}] {AiName} → {Sender}: {Reply}";
    public string SayCallerEchoFormat { get; set; } = "[AI Companion:Say] → {Sender} → {AiName}: {Prompt}";
    public string SayAiReplyFormat { get; set; } = "[AI Companion:Say] → [{AiName}] {AiName} → {Sender}: {Reply}";

    // Auto-reply toggles
    public bool PartyAutoReply { get; set; } = true;    // reply automatically in /party when trigger matches & whitelisted
    public bool SayAutoReply { get; set; } = false;   // disabled by default to be polite in open chat

    // ==== Outbound Chat Path ====
    // We support 3 ways to send messages:
    //  - IPC via ChatTwo (preferred if installed)
    //  - Fallback to /p or /say command dispatch via ICommandManager
    //  - Optional "typed" injection (only if your environment allows; default false)
    public bool UseIpcChatTwo { get; set; } = true;     // try ChatTwo IPC first if available
    public bool UseCommandDispatch { get; set; } = true;// use ICommandManager.ProcessCommand("/p ...")
    public bool UseTypedSend { get; set; } = false;     // leave false unless you know it's allowed in your setup

    // Network safety options
    public bool AsciiSafeNetwork { get; set; } = true;  // replace chars not in 0x20..0x7E before sending game-bound text

    // Chunking & streaming flush for /say (and also used for /party)
    public int SayChunkSize { get; set; } = 220;  // split long messages (<= 300, leave headroom)
    public int SayStreamFlushChars { get; set; } = 60;   // accumulate streamed tokens before flushing a chunk
    public int SayStreamMinFlushMs { get; set; } = 800;  // timelimit to force a flush

    // Debug tap
    public bool DebugChatTap { get; set; } = false;   // mirror incoming/outgoing to debug channel
    public int DebugChatTapLimit { get; set; } = 250;     // truncate long mirrors

    // ==== Memory ====
    public bool EnableMemory { get; set; } = true;
    public bool AutoSaveMemory { get; set; } = true;
    public int MaxMemories { get; set; } = 256;
    public string MemoriesFileRelative { get; set; } = "memories.json";

    // ==== Chronicle (Eternal Encore) ====
    public bool EnableChronicle { get; set; } = false;
    public string ChronicleFileRelative { get; set; } = "chronicle.json";
    public string ChronicleStyle { get; set; } = "short"; // short | verbose
    public int ChronicleMaxEntries { get; set; } = 200;
    public bool ChronicleAutoAppend { get; set; } = true;

    // ==== Internal wiring ====
    [NonSerialized] private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi) => this.pluginInterface = pi;

    public void Save() => this.pluginInterface?.SavePluginConfig(this);

    // ==== Helpers (paths & sanitization) ====
    public string GetConfigDirectory()
    {
        // If not yet initialized (rare), fall back to local folder
        return pluginInterface?.GetPluginConfigDirectory()
               ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiCompanionPlugin");
    }

    public string GetPersonaAbsolutePath(IDalamudPluginInterface pi)
        => EnsureDirJoin(PersonaFileRelative);

    public string GetMemoriesAbsolutePath()
        => EnsureDirJoin(MemoriesFileRelative);

    public string GetChronicleAbsolutePath()
        => EnsureDirJoin(ChronicleFileRelative);

    private string EnsureDirJoin(string relative)
    {
        var dir = GetConfigDirectory();
        try { Directory.CreateDirectory(dir); } catch { /* ignore */ }
        return Path.Combine(dir, relative);
    }

    public bool IsWhitelistedParty(string sender)
        => !RequireWhitelist || PartyWhitelist.Contains(sender);

    public bool IsWhitelistedSay(string sender)
        => !RequireWhitelist || SayWhitelist.Contains(sender);

    public string SanitizeForNetwork(string text)
    {
        if (!AsciiSafeNetwork || string.IsNullOrEmpty(text))
            return text ?? "";

        Span<char> buffer = text.ToCharArray();
        for (int i = 0; i < buffer.Length; i++)
        {
            var c = buffer[i];
            if (c < 0x20 || c > 0x7E)
                buffer[i] = '?';
        }
        return new string(buffer);
    }

    // Some presets for theme (optional helper)
    public void ApplyPresetIfMissing()
    {
        if (AccentColor is null && ThemeName.Equals("Void Touched", StringComparison.OrdinalIgnoreCase))
            AccentColor = new[] { 0.53f, 0.35f, 0.86f, 1.0f };   // void purple
        if (BgColor is null) BgColor = new[] { 0.06f, 0.05f, 0.08f, 1.0f };
        if (PanelColor is null) PanelColor = new[] { 0.10f, 0.07f, 0.14f, 0.95f };
        if (TextColor is null) TextColor = new[] { 0.92f, 0.90f, 0.96f, 1.0f };
    }
}
