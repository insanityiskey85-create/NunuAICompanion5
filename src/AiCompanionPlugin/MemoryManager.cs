using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using static AiCompanionPlugin.MemoryStore;

namespace AiCompanionPlugin;

public sealed class MemoryManager : IDisposable
{
    private readonly IDalamudPluginInterface pi;
    private readonly IPluginLog log;
    private readonly Configuration config;

    private readonly List<MemoryEntry> items = new();

    public MemoryManager(IDalamudPluginInterface pi, IPluginLog log, Configuration config)
    {
        this.pi = pi; this.log = log; this.config = config;
        Load();
    }

    public IReadOnlyList<MemoryEntry> Items => items;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    private string GetMemoriesPath()
    {
        var dir = pi.GetPluginConfigDirectory();
        Directory.CreateDirectory(dir);
        var name = string.IsNullOrWhiteSpace(config.MemoriesFileRelative) ? "memories.json" : config.MemoriesFileRelative;
        return Path.Combine(dir, name);
    }

    public void Add(string role, string content)
    {
        if (!config.EnableMemory) return;

        items.Add(new MemoryEntry(DateTime.UtcNow, role, content));

        // Trim if needed
        if (items.Count > Math.Max(0, config.MaxMemories))
        {
            var trim = items.Count - config.MaxMemories;
            if (trim > 0) items.RemoveRange(0, trim);
        }

        if (config.AutoSaveMemory) Save();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    public void Save()
    {
        try
        {
            var path = GetMemoriesPath();
            var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed saving memories.json");
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    public void Load()
    {
        try
        {
            var path = GetMemoriesPath();
            items.Clear();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var list = JsonSerializer.Deserialize<List<MemoryEntry>>(json) ?? new();
                items.AddRange(list);
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed loading memories.json");
        }
    }

    public void Clear()
    {
        items.Clear();
        Save();
    }

    public void Dispose()
    {
        if (config.EnableMemory && config.AutoSaveMemory)
            Save();
    }
}
