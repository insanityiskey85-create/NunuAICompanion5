// File: SettingsWindow.cs
using System.Numerics;
using Dalamud.Interface.Windowing;          // Window
using Dalamud.Bindings.ImGui;               // ImGui (Dalamud bindings)

namespace AiCompanionPlugin;

public sealed class SettingsWindow : Window
{
    private readonly Configuration config;
    private readonly PersonaManager persona;

    // IMPORTANT: API 13's Window constructor is (string, ImGuiWindowFlags)
    public SettingsWindow(Configuration config, PersonaManager persona)
        : base("AI Companion — Settings", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.config = config;
        this.persona = persona;

        // If your local Window lacks SizeConstraints, don't use it.
        // Layout will rely on AlwaysAutoResize + child regions.
    }

    public override void Draw()
    {
        // —— Backend ——————————————————————————————————————————————————————————
        ImGui.Text("Backend");
        ImGui.Separator();

        var baseUrl = config.BackendBaseUrl;
        if (ImGui.InputText("Base URL", ref baseUrl, 512))
        {
            config.BackendBaseUrl = baseUrl;
            config.Save();
        }

        var apiKey = config.ApiKey;
        if (ImGui.InputText("API Key", ref apiKey, 512, ImGuiInputTextFlags.Password))
        {
            config.ApiKey = apiKey;
            config.Save();
        }

        var model = config.Model;
        if (ImGui.InputText("Model", ref model, 128))
        {
            config.Model = model;
            config.Save();
        }

        ImGui.Spacing();

        // —— Chat ————————————————————————————————————————————————————————————
        ImGui.Text("Chat");
        ImGui.Separator();

        int maxHist = config.MaxHistoryMessages;
        if (ImGui.DragInt("Max History", ref maxHist, 1, 2, 64))
        {
            if (maxHist < 2) maxHist = 2;
            if (maxHist > 64) maxHist = 64;
            config.MaxHistoryMessages = maxHist;
            config.Save();
        }

        bool stream = config.StreamResponses;
        if (ImGui.Checkbox("Stream Responses (SSE)", ref stream))
        {
            config.StreamResponses = stream;
            config.Save();
        }

        ImGui.Spacing();

        // —— Persona ————————————————————————————————————————————————————————
        ImGui.Text("Persona");
        ImGui.Separator();

        var rel = config.PersonaFileRelative;
        if (ImGui.InputText("persona.txt (relative)", ref rel, 260))
        {
            config.PersonaFileRelative = rel;
            config.Save();
        }

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
            catch
            {
                // ignore
            }
        }

        ImGui.Spacing();

        // —— Theme (minimal; safe even if ThemeName isn't present) ————————
        ImGui.Text("Theme");
        ImGui.Separator();

        string themeName;
        try { themeName = (config as dynamic).ThemeName as string ?? "Default"; }
        catch { themeName = "Default"; }

        if (ImGui.InputText("Theme Name", ref themeName, 64))
        {
            try
            {
                (config as dynamic).ThemeName = themeName;
                config.Save();
            }
            catch
            {
                // If ThemeName isn't in config, ignore gracefully.
            }
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Private by default. No chat hooks unless explicitly enabled.");
    }
}

public class Window
{
}