using System;
using System.IO;
using System.Linq;
using System.Text;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Windowing;

namespace AiCompanionPlugin;

public sealed class SettingsWindow : Window
{
    private readonly Configuration config;
    private readonly PersonaManager persona;
    private readonly MemoryManager memory;
    private readonly ChronicleManager chronicle;

    private string status = string.Empty;

    // temp editor for whitelist
    private string whitelistEdit = string.Empty;

    public SettingsWindow(Configuration config, PersonaManager persona, MemoryManager memory, ChronicleManager chronicle)
        : base("AI Companion Settings", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.config = config;
        this.persona = persona;
        this.memory = memory;
        this.chronicle = chronicle;

        whitelistEdit = string.Join(Environment.NewLine, config.PartyWhitelist ?? new());
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
                if (ImGui.Selectable(keys[i], selected)) config.ThemeName = keys[i];
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.Spacing();

        // PERSONA
        ImGui.Text("Persona");
        ImGui.Separator();
        var rel = config.PersonaFileRelative ?? "persona.txt";
        if (ImGui.InputText("persona.txt (relative)", ref rel, 260)) { config.PersonaFileRelative = rel; }
        var abs = Plugin.PluginInterface != null ? config.GetPersonaAbsolutePath(Plugin.PluginInterface) : string.Empty;
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
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = abs, UseShellExecute = true, Verb = "open" });
                }
                catch { }
            }
            ImGui.SameLine();
            if (ImGui.Button("Open Folder"))
            {
                try
                {
                    var dir = Path.GetDirectoryName(abs)!;
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dir, UseShellExecute = true, Verb = "open" });
                }
                catch { }
            }
            ImGui.SameLine();
            if (ImGui.Button("Reload Persona")) { persona.Reload(); status = "Persona reloaded."; }
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
        ImGui.SameLine();
        if (ImGui.Button("Save Memories Now")) { memory.Save(); status = "Memories saved."; }
        ImGui.SameLine();
        if (ImGui.Button("Clear Memories")) { memory.Clear(); status = "Memories cleared."; }

        ImGui.Spacing();

        // CHRONICLE
        ImGui.Text("Eternal Encore — Chronicle");
        ImGui.Separator();
        bool enableChron = config.EnableChronicle;
        if (ImGui.Checkbox("Enable Chronicle", ref enableChron)) { config.EnableChronicle = enableChron; }
        bool autoChron = config.ChronicleAutoAppend;
        if (ImGui.Checkbox("Auto-append after each reply", ref autoChron)) { config.ChronicleAutoAppend = autoChron; }
        int maxChron = config.ChronicleMaxEntries;
        if (ImGui.SliderInt("Max Chronicle Entries", ref maxChron, 50, 10000)) { config.ChronicleMaxEntries = maxChron; }
        var chronPath = config.ChronicleFileRelative;
        if (ImGui.InputText("chronicle.json (relative)", ref chronPath, 256)) { config.ChronicleFileRelative = chronPath; }
        var style = config.ChronicleStyle ?? "Canon";
        if (ImGui.InputText("Style Tag", ref style, 64)) { config.ChronicleStyle = style; }
        if (ImGui.Button("Open Chronicle Window")) Plugin.OpenChronicleWindow();
        ImGui.SameLine();
        if (ImGui.Button("Save Chronicle Now")) { chronicle.Save(); status = "Chronicle saved."; }
        ImGui.SameLine();
        if (ImGui.Button("Clear Chronicle")) { chronicle.Clear(); status = "Chronicle cleared."; }

        ImGui.Spacing();

        // PARTY CHAT PIPE
        ImGui.Text("Party Chat Pipe (Outbound)");
        ImGui.Separator();
        bool enablePipe = config.EnablePartyPipe;
        if (ImGui.Checkbox("Enable Party Chat Posting (/p only)", ref enablePipe)) { config.EnablePartyPipe = enablePipe; }
        bool confirm = config.ConfirmBeforePartyPost;
        if (ImGui.Checkbox("Confirm before posting", ref confirm)) { config.ConfirmBeforePartyPost = confirm; }
        int chunk = config.PartyChunkSize;
        if (ImGui.SliderInt("Message Chunk Size", ref chunk, 100, 480)) { config.PartyChunkSize = chunk; }
        int delay = config.PartyPostDelayMs;
        if (ImGui.SliderInt("Delay Between Chunks (ms)", ref delay, 200, 2000)) { config.PartyPostDelayMs = delay; }

        ImGui.Spacing();

        // PARTY LISTENER (Inbound)
        ImGui.Text("Party Listener (Inbound)");
        ImGui.Separator();
        bool listener = config.EnablePartyListener;
        if (ImGui.Checkbox("Enable Party Listener (Party channel only)", ref listener)) { config.EnablePartyListener = listener; }
        var trig = config.PartyTrigger ?? "!AI Nunu";
        if (ImGui.InputText("Trigger", ref trig, 64)) { config.PartyTrigger = trig; }
        bool autoReply = config.PartyAutoReply;
        if (ImGui.Checkbox("Auto-reply when triggered", ref autoReply)) { config.PartyAutoReply = autoReply; }

        ImGui.Text("Whitelist (one name per line, no @World):");
        ImGui.InputTextMultiline("##whitelist", ref whitelistEdit, 8000, new System.Numerics.Vector2(400, 100));
        if (ImGui.Button("Save Whitelist"))
        {
            var lines = whitelistEdit.Replace("\r\n", "\n").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            config.PartyWhitelist = lines;
            status = $"Whitelist saved ({lines.Count} entries).";
        }

        ImGui.Spacing();

        if (ImGui.Button("Save Settings")) { config.Save(); status = "Settings saved."; }
        if (!string.IsNullOrEmpty(status))
        {
            ImGui.SameLine(); ImGui.TextDisabled(status);
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Listener scope: Party channel only. Triggers must start the message and sender must be on whitelist.");
    }
}
