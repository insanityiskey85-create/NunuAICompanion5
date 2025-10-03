using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace AiCompanionPlugin;

public sealed class Configuration // no IPluginConfiguration to avoid Dalamud version drift
{
    public int Version { get; set; } = 1;

    // AI/backend
    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:3001/v1";
    public string Model { get; set; } = "qwen2.5:1.5b-instruct-q4_K_M";
    public string ModelProvider { get; set; } = "OpenAI-compatible";

    // Persona / theme
    public string AiDisplayName { get; set; } = "AI Nunu";
    public string ThemeName { get; set; } = "Void Touched";
    public bool UseAsciiOnlyNetwork { get; set; } = false;

    // Memory
    public bool EnableMemory { get; set; } = true;
    public bool AutoSaveMemory { get; set; } = true;
    public int MaxMemories { get; set; } = 256;
    public string MemoriesFileRelative { get; set; } = "memories.json";

    // Chronicle
    public bool EnableChronicle { get; set; } = false;
    public string ChronicleFileRelative { get; set; } = "chronicle.txt";
    public string ChronicleStyle { get; set; } = "Eternal Encore";
    public int ChronicleMaxEntries { get; set; } = 500;
    public bool ChronicleAutoAppend { get; set; } = false;

    // Chat listening + triggers/whitelists
    public bool EnablePartyListener { get; set; } = true;
    public bool EnableSayListener { get; set; } = true;

    public string PartyTrigger { get; set; } = "!AI Nunu";
    public string SayTrigger { get; set; } = "!AI Nunu";

    public bool RequireWhitelist { get; set; } = false;
    public List<string> PartyWhitelist { get; set; } = new() { "Nunubu Nubu" };
    public List<string> SayWhitelist { get; set; } = new() { "Nunubu Nubu" };

    // Debug
    public bool DebugChatTap { get; set; } = false;
    public int DebugChatTapLimit { get; set; } = 200;

    // UI
    public bool ShowChatWindowOnStart { get; set; } = true;

    // Save/load
    private string ConfigPath(DalamudPluginInterface pi) => Path.Combine(pi.ConfigDirectory.FullName, "AiCompanionPlugin.json");

    public void Save(DalamudPluginInterface pi)
    {
        try
        {
            Directory.CreateDirectory(pi.ConfigDirectory.FullName);
            File.WriteAllText(ConfigPath(pi), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Plugin.Log.Error(e, "Failed to save configuration.");
        }
    }

    public static Configuration Load(DalamudPluginInterface pi)
    {
        try
        {
            var path = Path.Combine(pi.ConfigDirectory.FullName, "AiCompanionPlugin.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<Configuration>(json);
                if (cfg != null)
                    return cfg;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.Error(e, "Failed to load configuration, using defaults.");
        }
        return new Configuration();
    }

    internal void Save(IDalamudPluginInterface pluginInterface) => throw new NotImplementedException();

    public static explicit operator Configuration(IPluginConfiguration? v)
    {
        throw new NotImplementedException();
    }
}
