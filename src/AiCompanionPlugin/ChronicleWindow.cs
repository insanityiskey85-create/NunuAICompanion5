using System;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Interface.Windowing;

namespace AiCompanionPlugin;

public sealed class ChronicleWindow : Window
{
    private readonly Configuration config;
    private readonly ChronicleManager manager;

    private string search = string.Empty;
    private int selectedIndex = -1;
    private string exportStatus = string.Empty;

    public ChronicleWindow(Configuration config, ChronicleManager manager)
        : base("AI Nunu — Chronicle", ImGuiWindowFlags.None)
    {
        this.config = config;
        this.manager = manager;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public override void Draw()
    {
        var pops = ThemePalette.ApplyTheme(config.ThemeName ?? "Eorzean Night");

        ImGui.InputTextWithHint("##search", "Search text…", ref search, 512);
        ImGui.SameLine();
        if (ImGui.Button("Refresh")) manager.Load();
        ImGui.SameLine();
        if (ImGui.Button("Clear Chronicle")) { manager.Clear(); selectedIndex = -1; }

        ImGui.SameLine();
        if (ImGui.Button("Export .md"))
        {
            try
            {
                var md = manager.ExportMarkdown();
                var dir = Plugin.PluginInterface.GetPluginConfigDirectory();
                var path = System.IO.Path.Combine(dir, "chronicle.md");
                System.IO.File.WriteAllText(path, md, Encoding.UTF8);
                exportStatus = $"Exported to {path}";
            }
            catch (Exception ex)
            {
                exportStatus = $"Export failed: {ex.Message}";
            }
        }
        if (!string.IsNullOrEmpty(exportStatus))
        {
            ImGui.SameLine();
            ImGui.TextDisabled(exportStatus);
        }

        ImGui.Separator();
        ImGui.BeginChild("list", new Vector2(260, -2), true);

        var items = manager.Entries.Select((e, i) => (Entry: e, Index: i)).ToList();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLowerInvariant();
            items = items.Where(x =>
                (x.Entry.UserText?.ToLowerInvariant().Contains(s) ?? false) ||
                (x.Entry.AiText?.ToLowerInvariant().Contains(s) ?? false) ||
                (x.Entry.Model?.ToLowerInvariant().Contains(s) ?? false) ||
                (x.Entry.Style?.ToLowerInvariant().Contains(s) ?? false)
            ).ToList();
        }

        for (int it = items.Count - 1; it >= 0; it--) // newest first
        {
            var i = items[it].Index;
            var e = items[it].Entry;
            var label = $"{e.TimestampUtc.ToLocalTime():MM-dd HH:mm} · {Shorten(e.UserText)}";
            if (ImGui.Selectable(label, selectedIndex == i))
                selectedIndex = i;
        }
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("detail", new Vector2(0, -2), true);
        if (selectedIndex >= 0 && selectedIndex < manager.Entries.Count)
        {
            var e = manager.Entries[selectedIndex];
            ImGui.TextDisabled($"{e.TimestampUtc.ToLocalTime():yyyy-MM-dd HH:mm}  ·  Model: {e.Model}  ·  Style: {e.Style}");
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.95f, 1f), "You");
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted(e.UserText);
            ImGui.PopTextWrapPos();

            ImGui.Separator();

            var aiName = string.IsNullOrWhiteSpace(config.AiDisplayName) ? "AI Nunu" : config.AiDisplayName;
            ImGui.TextColored(new Vector4(0.85f, 0.8f, 1f, 1f), aiName);
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted(e.AiText);
            ImGui.PopTextWrapPos();
        }
        else
        {
            ImGui.TextDisabled("Select an entry to view details.");
        }
        ImGui.EndChild();

        ThemePalette.PopTheme(pops);
    }

    private static string Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "(no text)";
        var t = text.Replace("\r\n", " ").Replace("\n", " ");
        return t.Length > 36 ? t.Substring(0, 36) + "…" : t;
    }
}
