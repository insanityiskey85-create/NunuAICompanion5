// SPDX-License-Identifier: MIT
// AiCompanionPlugin - SettingsWindow.cs
namespace AiCompanionPlugin
{

    public class SettingsWindow
    {
        private readonly Configuration config;
        private readonly Action save;

        private bool isOpen = true;

        private string memoriesPath = string.Empty;
        private string chroniclePath = string.Empty;
        private string chronicleStyle = string.Empty;

        public SettingsWindow(Configuration config, Action save)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.save = save ?? throw new ArgumentNullException(nameof(save));

            memoriesPath = config.MemoriesFileRelative ?? "Data/memories.json";
            chroniclePath = config.ChronicleFileRelative ?? "Data/chronicle.md";
            chronicleStyle = config.ChronicleStyle ?? "journal";
        }

        public bool IsOpen { get => isOpen; set => isOpen = value; }

        public void Draw()
        {
            if (!isOpen) return;

            if (!ImGui.Begin("AI Companion — Settings", ref isOpen))
            {
                ImGui.End();
                return;
            }

            ImGui.TextDisabled("General");
            ImGui.Separator();

            ImGui.Text("AI Display Name:");
            ImGui.SameLine();
            var name = config.AiDisplayName;
            if (ImGui.InputText("##ai_name", ref name, 256))
            {
                config.AiDisplayName = string.IsNullOrWhiteSpace(name) ? "Nunu" : name.Trim();
                save();
            }

            ImGui.Text("Theme Name:");
            ImGui.SameLine();
            var theme = config.ThemeName;
            if (ImGui.InputText("##theme_name", ref theme, 256))
            {
                config.ThemeName = string.IsNullOrWhiteSpace(theme) ? "Default" : theme.Trim();
                save();
            }

            bool openChat = config.OpenChatOnLoad;
            if (ImGui.Checkbox("Open Chat on Load", ref openChat))
            {
                config.OpenChatOnLoad = openChat; save();
            }
            bool openSettings = config.OpenSettingsOnLoad;
            if (ImGui.Checkbox("Open Settings on Load", ref openSettings))
            {
                config.OpenSettingsOnLoad = openSettings; save();
            }

            ImGui.Separator();
            ImGui.TextDisabled("Memory");
            bool enableMemory = config.EnableMemory;
            if (ImGui.Checkbox("Enable Memory", ref enableMemory))
            { config.EnableMemory = enableMemory; save(); }
            bool auto = config.AutoSaveMemory;
            if (ImGui.Checkbox("Auto-Save Memory", ref auto))
            { config.AutoSaveMemory = auto; save(); }

            int maxMem = config.MaxMemories;
            if (ImGui.InputInt("Max Memories", ref maxMem))
            {
                if (maxMem < 0) maxMem = 0;
                config.MaxMemories = maxMem; save();
            }

            ImGui.Text("Memories file (relative):");
            ImGui.SameLine();
            if (ImGui.InputText("##mem_path", ref memoriesPath, 1024)) { }
            if (ImGui.Button("Apply Memories Path"))
            {
                config.MemoriesFileRelative = string.IsNullOrWhiteSpace(memoriesPath) ? "Data/memories.json" : memoriesPath.Trim();
                save();
            }

            ImGui.Separator();
            ImGui.TextDisabled("Chronicle");

            bool enableChronicle = config.EnableChronicle;
            if (ImGui.Checkbox("Enable Chronicle", ref enableChronicle))
            { config.EnableChronicle = enableChronicle; save(); }

            ImGui.Text("Style:");
            ImGui.SameLine();
            if (ImGui.InputText("##chron_style", ref chronicleStyle, 256)) { }

            int maxEntries = config.ChronicleMaxEntries;
            if (ImGui.InputInt("Max Entries", ref maxEntries))
            {
                if (maxEntries < 1) maxEntries = 1;
                config.ChronicleMaxEntries = maxEntries; save();
            }

            bool autoAppend = config.ChronicleAutoAppend;
            if (ImGui.Checkbox("Auto-Append", ref autoAppend))
            { config.ChronicleAutoAppend = autoAppend; save(); }

            ImGui.Text("Chronicle file (relative):");
            ImGui.SameLine();
            if (ImGui.InputText("##chron_path", ref chroniclePath, 1024)) { }
            if (ImGui.Button("Apply Chronicle Path"))
            {
                config.ChronicleStyle = string.IsNullOrWhiteSpace(chronicleStyle) ? "journal" : chronicleStyle.Trim();
                config.ChronicleFileRelative = string.IsNullOrWhiteSpace(chroniclePath) ? "Data/chronicle.md" : chroniclePath.Trim();
                save();
            }

            ImGui.End();
        }
    }
}
