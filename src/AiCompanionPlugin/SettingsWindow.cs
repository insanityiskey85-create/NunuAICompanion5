using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace AiCompanionPlugin;

public sealed class SettingsWindow : Window
{
    private readonly Configuration config;
    private readonly PersonaManager persona;

    public SettingsWindow(string name) : base(name)
    {
    }

    public SettingsWindow(Configuration config, PersonaManager persona)
        : base("AI Companion — Settings")
    {
        this.config = config;
        this.persona = persona;
        this.IsOpen = false;
    }

    public SettingsWindow(string name, ImGuiWindowFlags flags = ImGuiWindowFlags.None, bool forceMainWindow = false) : base(name, flags, forceMainWindow)
    {
    }

    public override void Draw()
    {
        ImGui.Text("Backend");
        ImGui.Separator();

        var baseUrl = config.BackendBaseUrl;
        if (ImGui.InputText("Base URL", ref baseUrl, 512)) { config.BackendBaseUrl = baseUrl; config.Save(); }

        var apiKey = config.ApiKey;
        if (ImGui.InputText("API Key", ref apiKey, 512, ImGuiInputTextFlags.Password)) { config.ApiKey = apiKey; config.Save(); }

        var model = config.Model;
        if (ImGui.InputText("Model", ref model, 128)) { config.Model = model; config.Save(); }

        ImGui.Separator();
        ImGui.Text("Chat");
        ImGui.Separator();

        var maxHist = config.MaxHistoryMessages;
        if (ImGui.DragInt("Max History", ref maxHist, 1, 2, 64))
        {
            if (maxHist < 2) maxHist = 2; if (maxHist > 64) maxHist = 64;
            config.MaxHistoryMessages = maxHist; config.Save();
        }

        var stream = config.StreamResponses;
        if (ImGui.Checkbox("Stream Responses (SSE)", ref stream)) { config.StreamResponses = stream; config.Save(); }

        ImGui.Separator();
        ImGui.Text("Persona");
        ImGui.Separator();

        var rel = config.PersonaFileRelative;
        if (ImGui.InputText("persona.txt (relative)", ref rel, 260)) { config.PersonaFileRelative = rel; config.Save(); }

        ImGui.TextWrapped("Edit persona.txt in the plugin config folder. Changes hot-reload.");
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

        ImGui.Separator();
        ImGui.TextDisabled("Private by default. Enable Party/Say routing from main window if needed.");
    }
}
