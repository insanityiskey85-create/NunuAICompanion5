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
            ImGui.Text("Backend");
            ImGui.Separator();

            var baseUrl = config.BackendBaseUrl;
            if (ImGui.InputText("Base URL", ref baseUrl, 512)) { config.BackendBaseUrl = baseUrl; config.Save(); }

            var apiKey = config.ApiKey;
            if (ImGui.InputText("API Key", ref apiKey, 512, ImGuiInputTextFlags.Password)) { config.ApiKey = apiKey; config.Save(); }

            var model = config.Model;
            if (ImGui.InputText("Model", ref model, 128)) { config.Model = model; config.Save(); }

            ImGui.Spacing();
            ImGui.Text("Chat Window");
            ImGui.Separator();

            int maxHist = config.MaxHistoryMessages;
            if (ImGui.SliderInt("Max History", ref maxHist, 2, 64)) { config.MaxHistoryMessages = maxHist; config.Save(); }

            bool stream = config.StreamResponses;
            if (ImGui.Checkbox("Stream Responses (SSE)", ref stream)) { config.StreamResponses = stream; config.Save(); }

            var aiName = config.AiDisplayName ?? "AI Nunu";
            if (ImGui.InputText("Display Name", ref aiName, 64)) { config.AiDisplayName = aiName; config.Save(); }

            ImGui.Spacing();
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
            ImGui.Text("Routing (Say / Party)");
            ImGui.Separator();

            // Party basic
            bool partyL = config.EnablePartyListener;
            if (ImGui.Checkbox("Enable Party Listener (/party receive)", ref partyL)) { config.EnablePartyListener = partyL; config.Save(); }
            bool partyP = config.EnablePartyPipe;
            if (ImGui.Checkbox("Enable Party Pipe (/party send)", ref partyP)) { config.EnablePartyPipe = partyP; config.Save(); }
            var ptrig = config.PartyTrigger ?? "!AI Nunu";
            if (ImGui.InputText("Party Trigger", ref ptrig, 64)) { config.PartyTrigger = ptrig; config.Save(); }

            // Say basic
            bool sayL = config.EnableSayListener;
            if (ImGui.Checkbox("Enable Say Listener (/say receive)", ref sayL)) { config.EnableSayListener = sayL; config.Save(); }
            bool sayP = config.EnableSayPipe;
            if (ImGui.Checkbox("Enable Say Pipe (/say send)", ref sayP)) { config.EnableSayPipe = sayP; config.Save(); }
            var strig = config.SayTrigger ?? "!AI Nunu";
            if (ImGui.InputText("Say Trigger", ref strig, 64)) { config.SayTrigger = strig; config.Save(); }

            ImGui.Spacing();

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
            if (ImGui.CollapsingHeader("Debug & Safety", ImGuiTreeNodeFlags.DefaultOpen))
            {
                bool dbg = config.DebugChatTap;
                if (ImGui.Checkbox("Debug: Log incoming chat (types + text)", ref dbg)) { config.DebugChatTap = dbg; config.Save(); }
                int cap = config.DebugChatTapLimit;
                if (ImGui.SliderInt("Debug log cap (entries)", ref cap, 10, 1000)) { config.DebugChatTapLimit = cap; config.Save(); }

                bool req = config.RequireWhitelist;
                if (ImGui.Checkbox("Require whitelist (uncheck to allow anyone if list empty)", ref req)) { config.RequireWhitelist = req; config.Save(); }

                bool ascii = config.NetworkAsciiOnly;
                if (ImGui.Checkbox("ASCII-only network text (avoids emojis/glyph issues)", ref ascii)) { config.NetworkAsciiOnly = ascii; config.Save(); }

                bool arrows = config.UseAsciiHeaders;
                if (ImGui.Checkbox("Use ASCII arrows in headers (-> instead of →)", ref arrows)) { config.UseAsciiHeaders = arrows; config.Save(); }
            }

            ImGui.Spacing();
            ImGui.TextDisabled("Tip: If lines come out too short/too many, increase *Stream Flush Chars* and *Min Flush (ms)*, or disable Debug logging.");
        }
        catch (System.Exception ex)
        {
            Plugin.PluginLog.Error(ex, "SettingsWindow.Draw failed");
            ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), "SettingsWindow error (see log).");
        }
    }
}
