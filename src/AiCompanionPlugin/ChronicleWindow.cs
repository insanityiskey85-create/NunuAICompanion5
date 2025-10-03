// SPDX-License-Identifier: MIT
// AiCompanionPlugin - ChronicleWindow.cs

#nullable enable
using Dalamud.Plugin;
using System.Numerics;
using System.Text;

// IMPORTANT: use Dalamud.Bindings.ImGui only; no ImGuiNET here.

namespace AiCompanionPlugin
{
    /// <summary>
    /// Chronicle editor/viewer. Stores entries in the path indicated by Configuration.ChronicleFileRelative,
    /// resolved against Dalamud's plugin ConfigDirectory.
    /// </summary>
    public sealed class ChronicleWindow
    {
        private readonly Configuration config;
        private readonly IDalamudPluginInterface pluginInterface;

        private bool isOpen = true;

        // Working buffers
        private string chroniclePath = string.Empty;
        private string contentBuffer = string.Empty;
        private string newEntryBuffer = string.Empty;

        // UI sizing
        private float contentHeight = 320f;
        private float entryHeight = 90f;

        public ChronicleWindow(Configuration config, IDalamudPluginInterface pluginInterface)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.pluginInterface = pluginInterface ?? throw new ArgumentNullException(nameof(pluginInterface));

            chroniclePath = ResolveChroniclePath();
            EnsureDirectoryExists();
            TryLoadFile();
        }

        public bool IsOpen
        {
            get => isOpen;
            set => isOpen = value;
        }

        public void Draw()
        {
            if (!isOpen)
                return;

            if (!ImGui.Begin($"Chronicle — {config.ChronicleStyle}", ref isOpen))
            {
                ImGui.End();
                return;
            }

            DrawPathRow();
            ImGui.Separator();

            DrawContentView();
            ImGui.Separator();

            DrawNewEntry();
            ImGui.Separator();

            DrawActions();

            ImGui.End();
        }

        // ---------------- UI Sections ----------------

        private void DrawPathRow()
        {
            ImGui.TextDisabled("File");
            ImGui.SameLine();
            ImGui.TextUnformatted(chroniclePath);

            ImGui.SameLine();
            if (ImGui.Button("Reload"))
            {
                TryLoadFile();
            }

            ImGui.SameLine();
            if (ImGui.Button("Open Folder"))
            {
                TryOpenFolder();
            }
        }

        private void DrawContentView()
        {
            ImGui.TextDisabled("Chronicle Content");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120f);
            ImGui.SliderFloat("##content_height", ref contentHeight, 200f, 800f, "Height: %.0f");

            if (ImGui.BeginChild("##chronicle_content", new Vector2(-1, contentHeight), true))
            {
                ImGui.PushTextWrapPos(0);
                ImGui.InputTextMultiline("##chronicle_content_text", ref contentBuffer, 1024 * 1024, new Vector2(-1, -1));
                ImGui.PopTextWrapPos();
                ImGui.EndChild();
            }
        }

        private void DrawNewEntry()
        {
            ImGui.TextDisabled("New Entry");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120f);
            ImGui.SliderFloat("##entry_height", ref entryHeight, 60f, 300f, "Height: %.0f");

            if (ImGui.BeginChild("##new_entry", new Vector2(-1, entryHeight), true))
            {
                ImGui.PushTextWrapPos(0);
                ImGui.InputTextMultiline("##new_entry_text", ref newEntryBuffer, 64 * 1024, new Vector2(-1, -1));
                ImGui.PopTextWrapPos();
                ImGui.EndChild();
            }
        }

        private void DrawActions()
        {
            if (ImGui.Button("Save Whole File"))
            {
                TrySaveFile();
            }

            ImGui.SameLine();
            if (ImGui.Button("Append Entry"))
            {
                AppendEntry();
            }

            ImGui.SameLine();
            if (ImGui.Button("Trim To Max Entries"))
            {
                TrimToMaxEntries();
                TrySaveFile();
            }

            ImGui.SameLine();
            ImGui.TextDisabled($"Max: {config.ChronicleMaxEntries} • Auto-Append: {(config.ChronicleAutoAppend ? "On" : "Off")}");
        }

        // ---------------- Behavior ----------------

        private string ResolveChroniclePath()
        {
            // Modern Dalamud: use ConfigDirectory (DirectoryInfo), not GetPluginConfigDirectory()
            var baseDir = pluginInterface.ConfigDirectory?.FullName ?? AppContext.BaseDirectory;
            var rel = config.ChronicleFileRelative;
            if (string.IsNullOrWhiteSpace(rel)) rel = "Data/chronicle.md";
            return Path.GetFullPath(Path.Combine(baseDir, rel));
        }

        private void EnsureDirectoryExists()
        {
            try
            {
                var dir = Path.GetDirectoryName(chroniclePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch { /* swallow; draw still works, saves will fail with message */ }
        }

        private void TryLoadFile()
        {
            try
            {
                contentBuffer = File.Exists(chroniclePath)
                    ? File.ReadAllText(chroniclePath, new UTF8Encoding(false))
                    : string.Empty;
            }
            catch (Exception ex)
            {
                contentBuffer = $"# Load error\n{ex.GetType().Name}: {ex.Message}\n";
            }
        }

        private void TrySaveFile()
        {
            try
            {
                EnsureDirectoryExists();
                File.WriteAllText(chroniclePath, contentBuffer ?? string.Empty, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                // reflect error inside the buffer so the user sees it immediately
                contentBuffer += $"\n\n<!-- Save error: {ex.GetType().Name}: {ex.Message} -->\n";
            }
        }

        private void AppendEntry()
        {
            var entry = (newEntryBuffer ?? string.Empty).Trim();
            if (entry.Length == 0)
                return;

            // Build stamped line(s) depending on style
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            string block = config.ChronicleStyle?.ToLowerInvariant() switch
            {
                "markdown" => $"- **{stamp}** — {entry}\n",
                "timeline" => $"[{stamp}] {entry}\n",
                // default "journal" style
                _ => $"## {stamp}\n{entry}\n"
            };

            if (!contentBuffer.EndsWith("\n"))
                contentBuffer += "\n";

            contentBuffer += block;

            // Auto-trim if configured
            if (config.ChronicleAutoAppend)
            {
                TrimToMaxEntries();
            }

            // Clear input on success
            newEntryBuffer = string.Empty;

            TrySaveFile();
        }

        private void TrimToMaxEntries()
        {
            int max = Math.Max(1, config.ChronicleMaxEntries);

            // A simple heuristic: split into logical entries by style and keep the newest N.
            // For "journal": entries start with lines beginning with "## "
            // For "markdown" and "timeline": entries are line-based items
            var style = config.ChronicleStyle?.ToLowerInvariant();
            if (style == "journal")
            {
                var lines = (contentBuffer ?? string.Empty).Replace("\r\n", "\n").Split('\n');
                var entries = new List<List<string>>();
                List<string>? current = null;

                foreach (var line in lines)
                {
                    if (line.StartsWith("## "))
                    {
                        if (current is not null) entries.Add(current);
                        current = new List<string> { line };
                    }
                    else
                    {
                        (current ??= new List<string>()).Add(line);
                    }
                }
                if (current is not null) entries.Add(current);

                // keep last N
                var trimmed = entries.Count > max ? entries.GetRange(Math.Max(0, entries.Count - max), Math.Min(max, entries.Count)) : entries;

                var sb = new StringBuilder(contentBuffer.Length);
                for (int i = 0; i < trimmed.Count; i++)
                {
                    foreach (var l in trimmed[i])
                        sb.AppendLine(l);
                    if (i < trimmed.Count - 1)
                        sb.AppendLine();
                }
                contentBuffer = sb.ToString();
            }
            else
            {
                // line-oriented styles: keep last N non-empty lines
                var lines = new List<string>();
                foreach (var l in (contentBuffer ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
                {
                    if (!string.IsNullOrWhiteSpace(l))
                        lines.Add(l);
                }

                if (lines.Count > max)
                    lines = lines.GetRange(Math.Max(0, lines.Count - max), Math.Min(max, lines.Count));

                contentBuffer = string.Join('\n', lines) + "\n";
            }
        }

        private void TryOpenFolder()
        {
            try
            {
                var folder = Path.GetDirectoryName(chroniclePath);
                if (string.IsNullOrEmpty(folder))
                    return;

                // Platform-agnostic attempt
                var path = folder;
#if WINDOWS
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
#else
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
#endif
            }
            catch
            {
                // Non-fatal; ignore
            }
        }
    }
}
