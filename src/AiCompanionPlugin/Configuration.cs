using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace AiCompanionPlugin;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    // Backend
    public string BackendBaseUrl { get; set; } = "http://127.0.0.1:3001";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "qwen2.5:1.5b-instruct-q4_K_M";

    // Display / Theme
    public string AiDisplayName { get; set; } = "AI Nunu";
    public string ThemeName { get; set; } = "Void Touched";

    // Persona
    public string PersonaFileRelative { get; set; } = "persona.txt";
    public string SystemPromptOverride { get; set; } = string.Empty;

    // Chat UX
    public int MaxHistoryMessages { get; set; } = 20;
    public bool StreamResponses { get; set; } = true;

    // Triggers / Whitelists
    public string SayTrigger { get; set; } = "!AI Nunu";
    public string PartyTrigger { get; set; } = "!AI Nunu";
    public bool RequireWhitelist { get; set; } = false;
    public string[] SayWhitelist { get; set; } = Array.Empty<string>();
    public string[] PartyWhitelist { get; set; } = Array.Empty<string>();

    // Debug
    public bool DebugChatTap { get; set; } = false;
    public int DebugChatTapLimit { get; set; } = 200;

    // Memory
    public bool EnableMemory { get; set; } = false;
    public bool AutoSaveMemory { get; set; } = true;
    public string MemoriesFileRelative { get; set; } = "memories.json";
    public int MaxMemories { get; set; } = 256;

    // Chronicle (Eternal Encore)
    public bool EnableChronicle { get; set; } = false;
    public string ChronicleFileRelative { get; set; } = "chronicle.txt";
    public string ChronicleStyle { get; set; } = "journal";
    public int ChronicleMaxEntries { get; set; } = 500;
    public bool ChronicleAutoAppend { get; set; } = false;

    [NonSerialized] private IDalamudPluginInterface? pluginInterface;
    public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;
    public void Save() => pluginInterface?.SavePluginConfig(this);

    public string GetPersonaAbsolutePath(IDalamudPluginInterface pi)
    {
        var dir = pi.GetPluginConfigDirectory();
        System.IO.Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, PersonaFileRelative);
    }
}
