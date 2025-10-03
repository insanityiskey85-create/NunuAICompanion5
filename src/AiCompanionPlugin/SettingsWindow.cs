using System;
using System.IO;
using System.Linq;
using Dalamud.Interface.Windowing;

namespace AiCompanionPlugin;

[method: System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
public sealed class SettingsWindow(Configuration config, PersonaManager persona, MemoryManager memory, ChronicleManager chronicle) : Window("AI Companion Settings", ImGuiWindowFlags.AlwaysAutoResize)
{
    private readonly Configuration config = config;
    private readonly PersonaManager persona = persona;
    private readonly MemoryManager memory = memory;
    private readonly ChronicleManager chronicle = chronicle;

    private string status = string.Empty;

    // temp editors
    private string partyWhitelistEdit = string.Join(Environment.NewLine, config.PartyWhitelist ?? []);
    private string sayWhitelistEdit = string.Join(Environment.NewLine, config.SayWhitelist ?? []);

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

        // PARTY — Pipe & Listener
        ImGui.Text("Party (/p) — Pipe & Listener");
        ImGui.Separator();
        bool enablePipeP = config.EnablePartyPipe;
        if (ImGui.Checkbox("Enable Party Pipe (send)", ref enablePipeP)) { config.EnablePartyPipe = enablePipeP; }
        int pChunk = config.PartyChunkSize;
        if (ImGui.SliderInt("Party Chunk Size", ref pChunk, 100, 480)) { config.PartyChunkSize = pChunk; }
        int pDelay = config.PartyPostDelayMs;
        if (ImGui.SliderInt("Party Delay (ms)", ref pDelay, 200, 2000)) { config.PartyPostDelayMs = pDelay; }

        bool listenerP = config.EnablePartyListener;
        if (ImGui.Checkbox("Enable Party Listener (receive)", ref listenerP)) { config.EnablePartyListener = listenerP; }
        var trigP = config.PartyTrigger ?? "!AI Nunu";
        if (ImGui.InputText("Party Trigger", ref trigP, 64)) { config.PartyTrigger = trigP; }
        bool autoP = config.PartyAutoReply;
        if (ImGui.Checkbox("Auto-reply (Party)", ref autoP)) { config.PartyAutoReply = autoP; }
        bool echoP = config.PartyEchoCallerPrompt;
        if (ImGui.Checkbox("Echo Caller Prompt (Party)", ref echoP)) { config.PartyEchoCallerPrompt = echoP; }
        ImGui.InputTextMultiline("Party Whitelist", ref partyWhitelistEdit, 8000, new System.Numerics.Vector2(400, 80));
        if (ImGui.Button("Save Party Whitelist"))
        {
            var lines = partyWhitelistEdit.Replace("\r\n", "\n").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            config.PartyWhitelist = lines;
            status = $"Party whitelist saved ({lines.Count}).";
        }

        ImGui.Spacing();

        // SAY — Pipe & Listener
        ImGui.Text("Say (/s) — Pipe & Listener");
        ImGui.Separator();
        bool enablePipeS = config.EnableSayPipe;
        if (ImGui.Checkbox("Enable Say Pipe (send)", ref enablePipeS)) { config.EnableSayPipe = enablePipeS; }
        int sChunk = config.SayChunkSize;
        if (ImGui.SliderInt("Say Chunk Size", ref sChunk, 100, 480)) { config.SayChunkSize = sChunk; }
        int sDelay = config.SayPostDelayMs;
        if (ImGui.SliderInt("Say Delay (ms)", ref sDelay, 200, 2000)) { config.SayPostDelayMs = sDelay; }

        bool listenerS = config.EnableSayListener;
        if (ImGui.Checkbox("Enable Say Listener (receive)", ref listenerS)) { config.EnableSayListener = listenerS; }
        var trigS = config.SayTrigger ?? "!AI Nunu";
        if (ImGui.InputText("Say Trigger", ref trigS, 64)) { config.SayTrigger = trigS; }
        bool autoS = config.SayAutoReply;
        if (ImGui.Checkbox("Auto-reply (Say)", ref autoS)) { config.SayAutoReply = autoS; }
        bool echoS = config.SayEchoCallerPrompt;
        if (ImGui.Checkbox("Echo Caller Prompt (Say)", ref echoS)) { config.SayEchoCallerPrompt = echoS; }
        ImGui.InputTextMultiline("Say Whitelist", ref sayWhitelistEdit, 8000, new System.Numerics.Vector2(400, 80));
        if (ImGui.Button("Save Say Whitelist"))
        {
            var lines = sayWhitelistEdit.Replace("\r\n", "\n").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            config.SayWhitelist = lines;
            status = $"Say whitelist saved ({lines.Count}).";
        }
        // ... keep your existing file; just add this block near the end before Save Settings ...

        ImGui.Spacing();
        ImGui.Text("Debug & Safety");
        ImGui.Separator();
        bool dbg = config.DebugChatTap;
        if (ImGui.Checkbox("Debug: Log incoming chat (types + text)", ref dbg)) { config.DebugChatTap = dbg; }
        int cap = config.DebugChatTapLimit;
        if (ImGui.SliderInt("Debug log cap", ref cap, 10, 1000)) { config.DebugChatTapLimit = cap; }
        bool req = config.RequireWhitelist;
        if (ImGui.Checkbox("Require whitelist (uncheck to allow anyone if list empty)", ref req)) { config.RequireWhitelist = req; }

        ImGui.Spacing();

        if (ImGui.Button("Save Settings")) { config.Save(); status = "Settings saved."; }
        if (!string.IsNullOrEmpty(status)) { ImGui.SameLine(); ImGui.TextDisabled(status); }

        ImGui.Spacing();
        ImGui.TextDisabled("Routing: /p and /s supported. Listeners obey trigger + whitelist. Streaming replies chunked & timed.");
    }
}
