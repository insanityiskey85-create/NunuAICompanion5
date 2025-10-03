using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

public sealed class MemoryManager : IDisposable
{
    private readonly IDalamudPluginInterface pi;
    private readonly IPluginLog log;
    private readonly Configuration cfg;

    private readonly object gate = new();
    private List<MemoryNote> notes = new();
    private string path = string.Empty;

    public IReadOnlyList<MemoryNote> Notes
    {
        get { lock (gate) return notes.AsReadOnly(); }
    }

    public MemoryManager(IDalamudPluginInterface pi, IPluginLog log, Configuration cfg)
    {
        this.pi = pi; this.log = log; this.cfg = cfg;
        path = GetPath();
        EnsureLoaded();
    }

    private string GetPath()
    {
        var dir = pi.GetPluginConfigDirectory();
        Directory.CreateDirectory(dir);
        var rel = string.IsNullOrWhiteSpace(cfg.MemoriesFileRelative) ? "memories.json" : cfg.MemoriesFileRelative;
        return Path.Combine(dir, rel);
    }

    private void EnsureLoaded()
    {
        try
        {
            if (!File.Exists(path))
            {
                Save(); // creates empty file
                log.Info($"[Memory] Created {path}");
                return;
            }

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<List<MemoryNote>>(json) ?? new List<MemoryNote>();
            lock (gate) notes = loaded;
            log.Info($"[Memory] Loaded {notes.Count} notes");
        }
        catch (Exception ex)
        {
            log.Error(ex, "[Memory] Failed to load memories.json; starting fresh.");
            lock (gate) notes = new();
            Save();
        }
    }

    public void AppendTurn(string userName, string userText, string aiName, string aiText)
    {
        if (!cfg.EnableMemory) return;

        var items = new List<MemoryNote>
        {
            new(DateTimeOffset.UtcNow, "user", userName, userText),
            new(DateTimeOffset.UtcNow, "assistant", aiName, aiText)
        };

        lock (gate)
        {
            notes.AddRange(items);
            Prune_NoLock();
        }

        if (cfg.AutoSaveMemory) Save();
    }

    public void AddNote(string role, string author, string text)
    {
        if (!cfg.EnableMemory) return;
        lock (gate)
        {
            notes.Add(new MemoryNote(DateTimeOffset.UtcNow, role, author, text));
            Prune_NoLock();
        }
        if (cfg.AutoSaveMemory) Save();
    }

    public void Save()
    {
        try
        {
            List<MemoryNote> snap;
            lock (gate) snap = new(notes);
            var json = JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            log.Error(ex, "[Memory] Save failed");
        }
    }

    public void Prune()
    {
        lock (gate) Prune_NoLock();
        Save();
    }

    private void Prune_NoLock()
    {
        var cap = Math.Max(16, cfg.MaxMemories);
        if (notes.Count > cap)
            notes.RemoveRange(0, notes.Count - cap);
    }

    public string GetFilePath() => path;

    public void Dispose() { /* nothing */ }
}

public sealed record MemoryNote(DateTimeOffset When, string Role, string Author, string Text);
