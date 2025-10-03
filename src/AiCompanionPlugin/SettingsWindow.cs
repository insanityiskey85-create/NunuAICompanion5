using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AiCompanionPlugin;

public sealed class SettingsWindow : Window
{
    private readonly Configuration config;
    private readonly PersonaManager persona;
    private readonly MemoryManager memory;
    private readonly ChronicleManager chronicle;

    // scratch inputs for whitelist editors
    private string partyAddName = string.Empty;
    private string sayAddName = string.Empty;
    private int partySelectedIndex = -1;
    private int saySelectedIndex = -1;
    private string partyBulk = string.Empty;
    private string sayBulk = string.Empty;

    public SettingsWindow(Configuration config, PersonaManager persona, MemoryManager memory, ChronicleManager chronicle)
        : base("AI Companion Settings", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.config = config;
        this.persona = persona;
        this.memory = memory;
        this.chronicle = chronicle;
    }

    public override void Draw()
    {
        try
        {
            // ===== Backend =====
            ImGui.Text("Backend");
            ImGui.Separator();

            var baseUrl = config.BackendBaseUrl;
            if (ImGui.InputText("Base URL", ref baseUrl, 512)) { config.BackendBaseUrl = baseUrl; config.Save(); }

            var apiKey = config.ApiKey;
            if (ImGui.InputText("API Key", ref apiKey, 512, ImGuiInputTextFlags.Password)) { config.ApiKey = apiKey; config.Save(); }

            var model = config.Model;
            if (ImGui.InputText("Model", ref model, 128)) { config.Model = model; config.Save(); }

            ImGui.Spacing();

            // ===== Chat Window =====
            ImGui.Text("Chat Window");
            ImGui.Separator();

            int maxHist = config.MaxHistoryMessages;
            if (ImGui.SliderInt("Max History", ref maxHist, 2, 64)) { config.MaxHistoryMessages = maxHist; config.Save(); }

            bool stream = config.StreamResponses;
            if (ImGui.Checkbox("Stream Responses (SSE)", ref stream)) { config.StreamResponses = stream; config.Save(); }

            var aiName = config.AiDisplayName ?? "AI Nunu";
            if (ImGui.InputText("Display Name", ref aiName, 64)) { config.AiDisplayName = aiName; config.Save(); }

            ImGui.Spacing();

            // ===== Persona =====
            ImGui.Text("Persona");
            ImGui.Separator();

            var rel = config.PersonaFileRelative;
            if (ImGui.InputText("persona.txt (relative)", ref rel, 260)) { config.PersonaFileRelative = rel; config.Save(); }

            if (ImGui.Button("Open Config Folder"))
            {
                try
                {
                    var dir = Plugin.PluginInterface.GetPluginConfigDirectory();
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
                catch { }
            }

            ImGui.Spacing();

            // ===== Memory =====
            ImGui.Text("Memory");
            ImGui.Separator();

            bool en = config.EnableMemory;
            if (ImGui.Checkbox("Enable memories.json", ref en)) { config.EnableMemory = en; config.Save(); }

            bool auto = config.AutoSaveMemory;
            if (ImGui.Checkbox("Autosave after turns", ref auto)) { config.AutoSaveMemory = auto; config.Save(); }

            int cap = config.MaxMemories;
            if (ImGui.SliderInt("Max memories kept", ref cap, 16, 2000)) { config.MaxMemories = cap; config.Save(); }

            if (ImGui.Button("Open memories.json"))
            {
                try
                {
                    var file = memory.GetFilePath();
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = file, UseShellExecute = true });
                }
                catch { }
            }
            ImGui.SameLine();
            if (ImGui.Button("Prune now")) { memory.Prune(); }
            ImGui.SameLine();
            if (ImGui.Button("Save now")) { memory.Save(); }

            ImGui.TextDisabled("Notes are appended per user→AI turn when enabled.");

            ImGui.Spacing();

            // ===== Routing (Say / Party) =====
            ImGui.Text("Routing (Say / Party)");
            ImGui.Separator();

            // Party toggles
            bool partyL = config.EnablePartyListener;
            if (ImGui.Checkbox("Enable Party Listener (/party receive)", ref partyL)) { config.EnablePartyListener = partyL; config.Save(); }

            bool partyP = config.EnablePartyPipe;
            if (ImGui.Checkbox("Enable Party Pipe (/party send)", ref partyP)) { config.EnablePartyPipe = partyP; config.Save(); }

            var ptrig = config.PartyTrigger ?? "!AI Nunu";
            if (ImGui.InputText("Party Trigger", ref ptrig, 64)) { config.PartyTrigger = ptrig; config.Save(); }

            // Say toggles
            bool sayL = config.EnableSayListener;
            if (ImGui.Checkbox("Enable Say Listener (/say receive)", ref sayL)) { config.EnableSayListener = sayL; config.Save(); }

            bool sayP = config.EnableSayPipe;
            if (ImGui.Checkbox("Enable Say Pipe (/say send)", ref sayP)) { config.EnableSayPipe = sayP; config.Save(); }

            var strig = config.SayTrigger ?? "!AI Nunu";
            if (ImGui.InputText("Say Trigger", ref strig, 64)) { config.SayTrigger = strig; config.Save(); }

            ImGui.Spacing();

            // ===== Whitelists =====
            DrawWhitelistEditors();

            ImGui.Spacing();

            // ===== Advanced Streaming & Chunking =====
            if (ImGui.CollapsingHeader("Advanced Streaming & Chunking", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.TextDisabled("Say (/say)");
                int sayChunk = config.SayChunkSize;
                if (ImGui.SliderInt("Say Chunk Size (chars)", ref sayChunk, 120, 480)) { config.SayChunkSize = sayChunk; config.Save(); }

                int sayDelay = config.SayPostDelayMs;
                if (ImGui.SliderInt("Say Post Delay (ms)", ref sayDelay, 100, 1500)) { config.SayPostDelayMs = sayDelay; config.Save(); }

                int sayFlushChars = config.SayStreamFlushChars;
                if (ImGui.SliderInt("Say Stream Flush Chars", ref sayFlushChars, 80, 480)) { config.SayStreamFlushChars = sayFlushChars; config.Save(); }

                int sayFlushMs = config.SayStreamMinFlushMs;
                if (ImGui.SliderInt("Say Stream Min Flush (ms)", ref sayFlushMs, 200, 2000)) { config.SayStreamMinFlushMs = sayFlushMs; config.Save(); }

                ImGui.Separator();

                ImGui.TextDisabled("Party (/party)");
                int pChunk = config.PartyChunkSize;
                if (ImGui.SliderInt("Party Chunk Size (chars)", ref pChunk, 120, 480)) { config.PartyChunkSize = pChunk; config.Save(); }

                int pDelay = config.PartyPostDelayMs;
                if (ImGui.SliderInt("Party Post Delay (ms)", ref pDelay, 100, 1500)) { config.PartyPostDelayMs = pDelay; config.Save(); }

                int pFlushChars = config.PartyStreamFlushChars;
                if (ImGui.SliderInt("Party Stream Flush Chars", ref pFlushChars, 80, 480)) { config.PartyStreamFlushChars = pFlushChars; config.Save(); }

                int pFlushMs = config.PartyStreamMinFlushMs;
                if (ImGui.SliderInt("Party Stream Min Flush (ms)", ref pFlushMs, 200, 2000)) { config.PartyStreamMinFlushMs = pFlushMs; config.Save(); }
            }

            ImGui.Spacing();

            // ===== Debug & Safety =====
            if (ImGui.CollapsingHeader("Debug & Safety", ImGuiTreeNodeFlags.DefaultOpen))
            {
                bool dbg = config.DebugChatTap;
                if (ImGui.Checkbox("Debug: Log incoming chat (types + text)", ref dbg)) { config.DebugChatTap = dbg; config.Save(); }

                int dbgCap = config.DebugChatTapLimit;
                if (ImGui.SliderInt("Debug log cap (entries)", ref dbgCap, 10, 1000)) { config.DebugChatTapLimit = dbgCap; config.Save(); }

                bool req = config.RequireWhitelist;
                if (ImGui.Checkbox("Require whitelist for auto-replies", ref req)) { config.RequireWhitelist = req; config.Save(); }

                bool ascii = config.NetworkAsciiOnly;
                if (ImGui.Checkbox("ASCII-only network text", ref ascii)) { config.NetworkAsciiOnly = ascii; config.Save(); }

                bool arrows = config.UseAsciiHeaders;
                if (ImGui.Checkbox("Use ASCII arrows in headers (->)", ref arrows)) { config.UseAsciiHeaders = arrows; config.Save(); }

                ImGui.TextDisabled("Case-sensitive names. Exact match required.");
            }
        }
        catch (Exception ex)
        {
            Plugin.PluginLog.Error(ex, "SettingsWindow.Draw failed");
            ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), "SettingsWindow error (see log).");
        }
    }

    // ---------- helpers ----------
    private void DrawWhitelistEditors()
    {
        ImGui.Text("Whitelists");
        ImGui.Separator();

        // Party
        ImGui.TextDisabled("Party whitelist");
        ImGui.BeginChild("party-wl", new Vector2(360, 160), true);
        var partyList = config.PartyWhitelist ??= new List<string>();
        for (int i = 0; i < partyList.Count; i++)
        {
            var isSel = (i == partySelectedIndex);
            if (ImGui.Selectable(partyList[i], isSel))
                partySelectedIndex = i;
        }
        ImGui.EndChild();

        ImGui.InputText("Add Party Name", ref partyAddName, 64);
        ImGui.SameLine();
        if (ImGui.Button("Add##party") && !string.IsNullOrWhiteSpace(partyAddName))
        {
            var cleaned = CleanName(partyAddName);
            if (!string.IsNullOrEmpty(cleaned) && !partyList.Contains(cleaned))
            {
                partyList.Add(cleaned);
                partyList.Sort(StringComparer.Ordinal);
                config.Save();
            }
            partyAddName = string.Empty;
        }
        ImGui.SameLine();
        if (ImGui.Button("Remove Selected##party") && partySelectedIndex >= 0 && partySelectedIndex < partyList.Count)
        {
            partyList.RemoveAt(partySelectedIndex);
            partySelectedIndex = -1;
            config.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear##party") && partyList.Count > 0)
        {
            partyList.Clear();
            partySelectedIndex = -1;
            config.Save();
        }

        ImGui.InputTextMultiline("Bulk Import (one per line)##party", ref partyBulk, 2000, new Vector2(360, 80));
        if (ImGui.Button("Import##party"))
        {
            var names = ParseBulk(partyBulk);
            if (names.Count > 0)
            {
                foreach (var n in names)
                    if (!partyList.Contains(n)) partyList.Add(n);
                partyList.Sort(StringComparer.Ordinal);
                config.Save();
                partyBulk = string.Empty;
            }
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Say
        ImGui.TextDisabled("Say whitelist");
        ImGui.BeginChild("say-wl", new Vector2(360, 160), true);
        var sayList = config.SayWhitelist ??= new List<string>();
        for (int i = 0; i < sayList.Count; i++)
        {
            var isSel = (i == saySelectedIndex);
            if (ImGui.Selectable(sayList[i], isSel))
                saySelectedIndex = i;
        }
        ImGui.EndChild();

        ImGui.InputText("Add Say Name", ref sayAddName, 64);
        ImGui.SameLine();
        if (ImGui.Button("Add##say") && !string.IsNullOrWhiteSpace(sayAddName))
        {
            var cleaned = CleanName(sayAddName);
            if (!string.IsNullOrEmpty(cleaned) && !sayList.Contains(cleaned))
            {
                sayList.Add(cleaned);
                sayList.Sort(StringComparer.Ordinal);
                config.Save();
            }
            sayAddName = string.Empty;
        }
        ImGui.SameLine();
        if (ImGui.Button("Remove Selected##say") && saySelectedIndex >= 0 && saySelectedIndex < sayList.Count)
        {
            sayList.RemoveAt(saySelectedIndex);
            saySelectedIndex = -1;
            config.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear##say") && sayList.Count > 0)
        {
            sayList.Clear();
            saySelectedIndex = -1;
            config.Save();
        }

        ImGui.InputTextMultiline("Bulk Import (one per line)##say", ref sayBulk, 2000, new Vector2(360, 80));
        if (ImGui.Button("Import##say"))
        {
            var names = ParseBulk(sayBulk);
            if (names.Count > 0)
            {
                foreach (var n in names)
                    if (!sayList.Contains(n)) sayList.Add(n);
                sayList.Sort(StringComparer.Ordinal);
                config.Save();
                sayBulk = string.Empty;
            }
        }
    }

    private static string CleanName(string s)
        => (s ?? string.Empty).Trim();

    private static List<string> ParseBulk(string text)
        => (text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(CleanName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
