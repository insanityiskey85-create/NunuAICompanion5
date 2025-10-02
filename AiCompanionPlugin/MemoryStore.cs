using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin;

namespace AiCompanionPlugin;

public sealed class MemoryStore : IDisposable
{
    private readonly IDalamudPluginInterface pi;
    private readonly string filePath;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public MemoryStore(IDalamudPluginInterface pi, string relativeFile)
    {
        this.pi = pi;
        var dir = pi.GetPluginConfigDirectory();
        Directory.CreateDirectory(dir);
        filePath = Path.Combine(dir, relativeFile);
        EnsureFile();
    }

    public void Dispose() => gate.Dispose();

    public sealed class MemoryEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
        public string Role { get; set; } = "note";         // "user" | "assistant" | "system" | "note"
        public string Text { get; set; } = string.Empty;
        public string[] Tags { get; set; } = Array.Empty<string>();
    }

    // Append a memory (trim file to MaxCount if provided)
    public async Task AppendAsync(MemoryEntry entry, int? maxCount = null, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var list = await ReadAllUnsafeAsync(token).ConfigureAwait(false);
            list.Add(entry);
            if (maxCount is int cap && cap > 0 && list.Count > cap)
                list = list.Skip(list.Count - cap).ToList();

            await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(list, jsonOptions), token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async Task<List<MemoryEntry>> LoadAllAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await ReadAllUnsafeAsync(token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async Task DeleteAsync(Guid id, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var list = await ReadAllUnsafeAsync(token).ConfigureAwait(false);
            list.RemoveAll(m => m.Id == id);
            await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(list, jsonOptions), token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await File.WriteAllTextAsync(filePath, "[]", token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public string GetPath() => filePath;

    private void EnsureFile()
    {
        if (!File.Exists(filePath))
            File.WriteAllText(filePath, "[]");
    }

    private async Task<List<MemoryEntry>> ReadAllUnsafeAsync(CancellationToken token)
    {
        if (!File.Exists(filePath)) return new();
        var json = await File.ReadAllTextAsync(filePath, token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var data = JsonSerializer.Deserialize<List<MemoryEntry>>(json, jsonOptions);
            return data ?? new();
        }
        catch
        {
            // Corrupt file? Back up and reset.
            var backup = filePath + ".bak_" + DateTimeOffset.Now.ToUnixTimeSeconds();
            try { File.Copy(filePath, backup, overwrite: false); } catch { /* ignore */ }
            File.WriteAllText(filePath, "[]");
            return new();
        }
    }
}
