using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

public sealed class ChronicleManager : IDisposable
{
    private readonly IDalamudPluginInterface pi;
    private readonly IPluginLog log;
    private readonly Configuration config;

    private readonly List<ChronicleEntry> entries = new();

    public ChronicleManager(IDalamudPluginInterface pi, IPluginLog log, Configuration config)
    {
        this.pi = pi; this.log = log; this.config = config;
        Load();
    }

    public IReadOnlyList<ChronicleEntry> Entries => entries;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    private string GetChroniclePath()
    {
        var dir = pi.GetPluginConfigDirectory();
        Directory.CreateDirectory(dir);
        var file = string.IsNullOrWhiteSpace(config.ChronicleFileRelative) ? "chronicle.json" : config.ChronicleFileRelative;
        return Path.Combine(dir, file);
    }

    public void AppendExchange(string userText, string aiText, string model)
    {
        if (!config.EnableChronicle) return;

        entries.Add(new ChronicleEntry(
            TimestampUtc: DateTime.UtcNow,
            UserText: userText ?? string.Empty,
            AiText: aiText ?? string.Empty,
            Model: model ?? string.Empty,
            Style: config.ChronicleStyle ?? string.Empty
        ));

        // cap list
        var cap = Math.Max(10, config.ChronicleMaxEntries);
        if (entries.Count > cap)
            entries.RemoveRange(0, entries.Count - cap);

        if (config.ChronicleAutoAppend)
            Save();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    public void Save()
    {
        try
        {
            var path = GetChroniclePath();
            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed saving chronicle.json");
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    public void Load()
    {
        try
        {
            entries.Clear();
            var path = GetChroniclePath();
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path, Encoding.UTF8);
            var list = JsonSerializer.Deserialize<List<ChronicleEntry>>(json) ?? new();
            entries.AddRange(list);
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed loading chronicle.json");
        }
    }

    public void Clear()
    {
        entries.Clear();
        Save();
    }

    public string ExportMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# AI Nunu — Eternal Encore Chronicle");
        sb.AppendLine();
        foreach (var e in entries)
        {
            var when = e.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            sb.AppendLine($"## {when}  ·  Model: `{e.Model}`  ·  Style: `{e.Style}`");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(e.UserText))
            {
                sb.AppendLine("**You:**");
                sb.AppendLine();
                sb.AppendLine(Indent(e.UserText.Trim(), "> "));
                sb.AppendLine();
            }
            if (!string.IsNullOrWhiteSpace(e.AiText))
            {
                sb.AppendLine("**AI Nunu:**");
                sb.AppendLine();
                sb.AppendLine(Indent(e.AiText.Trim(), "> "));
                sb.AppendLine();
            }
            sb.AppendLine("---");
        }
        return sb.ToString();
    }

    private static string Indent(string text, string prefix)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        return string.Join("\n", lines.Select(l => prefix + l));
    }

    public void Dispose()
    {
        if (config.EnableChronicle && config.ChronicleAutoAppend)
            Save();
    }
}
