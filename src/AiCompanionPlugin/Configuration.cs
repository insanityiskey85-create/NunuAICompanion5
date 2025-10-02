using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace AiCompanionPlugin;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;

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

    // Memory (per-message)
    public bool EnableMemory { get; set; } = true;
    public bool AutoSaveMemory { get; set; } = true;
    public int MaxMemories { get; set; } = 256;
    public string MemoriesFileRelative { get; set; } = "memories.json";

    // Display
    public string AiDisplayName { get; set; } = "AI Nunu";

    // Chronicle (Eternal Encore)
    public bool EnableChronicle { get; set; } = true;
    public bool ChronicleAutoAppend { get; set; } = true;
    public int ChronicleMaxEntries { get; set; } = 1000;
    public string ChronicleFileRelative { get; set; } = "chronicle.json";
    public string ChronicleStyle { get; set; } = "Canon";

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
