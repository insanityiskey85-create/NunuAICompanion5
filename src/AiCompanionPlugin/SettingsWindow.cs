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
            ImGui.Text("Chat");
            ImGui.Separator();

            int maxHist = config.MaxHistoryMessages;
            if (ImGui.SliderInt("Max History", ref maxHist, 2, 64)) { config.MaxHistoryMessages = maxHist; config.Save(); }

            bool stream = config.StreamResponses;
            if (ImGui.Checkbox("Stream Responses (SSE)", ref stream)) { config.StreamResponses = stream; config.Save(); }

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

            bool partyL = config.EnablePartyListener;
            if (ImGui.Checkbox("Enable Party Listener", ref partyL)) { config.EnablePartyListener = partyL; config.Save(); }
            bool partyP = config.EnablePartyPipe;
            if (ImGui.Checkbox("Enable Party Pipe (send)", ref partyP)) { config.EnablePartyPipe = partyP; config.Save(); }
            var ptrig = config.PartyTrigger;
            if (ImGui.InputText("Party Trigger", ref ptrig, 64)) { config.PartyTrigger = ptrig; config.Save(); }

            bool sayL = config.EnableSayListener;
            if (ImGui.Checkbox("Enable Say Listener", ref sayL)) { config.EnableSayListener = sayL; config.Save(); }
            bool sayP = config.EnableSayPipe;
            if (ImGui.Checkbox("Enable Say Pipe (send)", ref sayP)) { config.EnableSayPipe = sayP; config.Save(); }
            var strig = config.SayTrigger;
            if (ImGui.InputText("Say Trigger", ref strig, 64)) { config.SayTrigger = strig; config.Save(); }

            ImGui.Spacing();
            ImGui.TextDisabled("No global chat hooks unless enabled above.");
        }
        catch (System.Exception ex)
        {
            Plugin.PluginLog.Error(ex, "SettingsWindow.Draw failed");
            ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), "SettingsWindow error (see log).");
        }
    }
}
