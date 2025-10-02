using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace AiCompanionPlugin;

public sealed class SettingsWindow : Window
{
    private readonly Configuration config;
    private readonly PersonaManager persona;

    private int themeIndex = 0;
    private string[] themeNames = System.Array.Empty<string>();

    public SettingsWindow(Configuration config, PersonaManager persona) : base("AI Companion Settings", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.config = config;
        this.persona = persona;
        themeNames = new System.Collections.Generic.List<string>(ThemePalette.Presets.Keys).ToArray();
        for (int i = 0; i < themeNames.Length; i++)
            if (themeNames[i] == config.SelectedTheme) { themeIndex = i; break; }
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

        ImGui.Spacing();
        ImGui.Text("Chat");
        ImGui.Separator();

        int maxHist = config.MaxHistoryMessages;
        if (ImGui.SliderInt("Max History", ref maxHist, 2, 64)) { config.MaxHistoryMessages = maxHist; config.Save(); }

        bool stream = config.StreamResponses;
        if (ImGui.Checkbox("Stream Responses (SSE)", ref stream)) { config.StreamResponses = stream; config.Save(); }

        ImGui.Spacing();
        ImGui.Text("Appearance");
        ImGui.Separator();

        if (ImGui.Combo("Theme", ref themeIndex, themeNames, themeNames.Length))
        {
            config.SelectedTheme = themeNames[themeIndex];
            config.Save();
        }

        ImGui.Spacing();
        ImGui.Text("Persona");
        ImGui.Separator();

        var rel = config.PersonaFileRelative;
        if (ImGui.InputText("persona.txt (relative)", ref rel, 260)) { config.PersonaFileRelative = rel; config.Save(); }

        ImGui.TextWrapped("Edit your persona.txt in the plugin config folder. Changes hot-reload.");
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
        ImGui.TextDisabled("No whitelist. No chat channel hooks. Private window only.");
    }
}
