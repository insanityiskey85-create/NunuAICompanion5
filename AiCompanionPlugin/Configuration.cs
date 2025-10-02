using System;
using Dalamud.Plugin;

namespace AiCompanionPlugin;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Backend settings
    public string BackendBaseUrl { get; set; } = "http://127.0.0.1:11434"; // e.g., Ollama or local proxy
    public string ApiKey { get; set; } = string.Empty; // if your backend needs one
    public string Model { get; set; } = "llama3.1";   // model name as your backend expects

    // Persona
    public string PersonaFileRelative { get; set; } = "persona.txt"; // relative to plugin config directory
    public string SystemPromptOverride { get; set; } = string.Empty;  // last read persona text cached

    // Chat UX
    public int MaxHistoryMessages { get; set; } = 20; // how many messages to send with each request
    public bool StreamResponses { get; set; } = true; // SSE streaming if the backend supports it

    // Theme
    public string SelectedTheme { get; set; } = "Eorzean Night"; // default preset

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
