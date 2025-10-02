using System;
using System.IO;
using System.Linq;
using Dalamud.Interface.Windowing;

namespace AiCompanionPlugin;

public sealed class SettingsWindow : Window
{
    private readonly Configuration config;
    private readonly PersonaManager persona;
    private readonly MemoryManager memory;

    private string status = string.Empty;

    public SettingsWindow(Configuration config, PersonaManager persona, MemoryManager memory)
        : base("AI Companion Settings", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.config = config;
        this.persona = persona;
        this.memory = memory;
    }

    public override void Draw()
    {
        // BACKEND
        ImGui.Text("Backend");
        ImGui.Separator();
        var baseUrl = config.BackendBaseUrl;
        if (ImGui.InputText("Base URL", ref baseUrl, 512)) { config.BackendBaseUrl = baseUrl; }
        var apiKey = config.ApiKey;
        if (ImGui.InputText("API Key", ref apiKey, 512, ImGuiInputTextFlags.Password)) { config.ApiKey = apiKey; }
        var model = config.Model;
        if (ImGui.InputText("Model", ref model, 128)) { config.Model = model; }

        ImGui.Spacing();

        // CHAT
        ImGui.Text("Chat");
        ImGui.Separator();
        int maxHist = config.MaxHistoryMessages;
        if (ImGui.SliderInt("Max History", ref maxHist, 2, 64)) { config.MaxHistoryMessages = maxHist; }
        bool stream = config.StreamResponses;
        if (ImGui.Checkbox("Stream Responses (SSE)", ref stream)) { config.StreamResponses = stream; }

        // Display name
        var aiName = config.AiDisplayName ?? "AI Nunu";
        if (ImGui.InputText("AI Display Name", ref aiName, 64)) { config.AiDisplayName = aiName; }

        ImGui.Spacing();

        // THEME
        ImGui.Text("Theme");
        ImGui.Separator();
        var currentTheme = config.ThemeName ?? "Eorzean Night";
        var keys = ThemePalette.Presets.Keys.ToArray();
        int idx = Array.IndexOf(keys, currentTheme);
        if (idx < 0) idx = 0;

        if (ImGui.BeginCombo("Chat Theme", keys[idx]))
        {
            for (int i = 0; i < keys.Length; i++)
            {
                bool selected = i == idx;
                if (ImGui.Selectable(keys[i], selected))
                    config.ThemeName = keys[i];
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.Spacing();

        // PERSONA
        ImGui.Text("Persona");
        ImGui.Separator();
        var rel = config.PersonaFileRelative ?? "persona.txt";
        if (ImGui.InputText("persona.txt (relative)", ref rel, 260))
        {
            config.PersonaFileRelative = rel;
        }

        // Show absolute path + quick actions
        var abs = Plugin.PluginInterface != null
            ? config.GetPersonaAbsolutePath(Plugin.PluginInterface)
            : string.Empty;

        if (!string.IsNullOrEmpty(abs))
        {
            ImGui.TextDisabled(abs);
            ImGui.SameLine();
            if (ImGui.Button("Open File"))
            {
                try
                {
                    if (!File.Exists(abs))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
                        File.WriteAllText(abs,
                            "You are AI Nunu, a helpful, strictly isolated personal AI companion.\n" +
                            "Stay within this private window. Do not read or write to game chat.\n" +
                            "Style: concise, kind, attentive to the user's goals.\n");
                    }
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = abs,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
                catch { }
            }
            ImGui.SameLine();
            if (ImGui.Button("Open Folder"))
            {
                try
                {
                    var dir = Path.GetDirectoryName(abs)!;
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
                catch { }
            }
            ImGui.SameLine();
            if (ImGui.Button("Reload Persona"))
            {
                try
                {
                    persona.Reload();
                    status = "Persona reloaded.";
                }
                catch
                {
                    status = "Failed to reload persona.";
                }
            }
        }

        ImGui.Spacing();

        // MEMORY
        ImGui.Text("Memory");
        ImGui.Separator();
        bool enable = config.EnableMemory;
        if (ImGui.Checkbox("Enable Memory", ref enable)) { config.EnableMemory = enable; }
        bool autosave = config.AutoSaveMemory;
        if (ImGui.Checkbox("Auto-save on new messages", ref autosave)) { config.AutoSaveMemory = autosave; }
        int maxMem = config.MaxMemories;
        if (ImGui.SliderInt("Max Memories", ref maxMem, 16, 4096)) { config.MaxMemories = maxMem; }
        var memPath = config.MemoriesFileRelative;
        if (ImGui.InputText("memories.json (relative)", ref memPath, 256)) { config.MemoriesFileRelative = memPath; }

        if (ImGui.Button("Save Memories Now")) { memory.Save(); status = "Memories saved."; }
        ImGui.SameLine();
        if (ImGui.Button("Clear Memories")) { memory.Clear(); status = "Memories cleared."; }

        ImGui.Spacing();
        if (ImGui.Button("Save Settings")) { config.Save(); status = "Settings saved."; }
        if (!string.IsNullOrEmpty(status))
        {
            ImGui.SameLine();
            ImGui.TextDisabled(status);
        }

        ImGui.Spacing();
        ImGui.TextDisabled("No whitelist. No chat channel hooks. Private window only.");
    }
}
