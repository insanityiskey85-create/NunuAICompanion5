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
    private readonly string filePath;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public MemoryStore(IDalamudPluginInterface pi, string relativeFile)
    {
        var dir = pi.GetPluginConfigDirectory();
        Directory.CreateDirectory(dir);
        filePath = Path.Combine(dir, relativeFile);
        if (!File.Exists(filePath)) File.WriteAllText(filePath, "[]");
    }

    public void Dispose() => gate.Dispose();

    public sealed class MemoryEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
        public string Role { get; set; } = "note";
        public string Text { get; set; } = string.Empty;
        public string[] Tags { get; set; } = Array.Empty<string>();
    }

    public string GetPath() => filePath;

    public async Task AppendAsync(MemoryEntry entry, int? maxCount = null, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var list = await ReadAllUnsafeAsync(token);
            list.Add(entry);
            if (maxCount is int cap && cap > 0 && list.Count > cap)
                list = list.Skip(list.Count - cap).ToList();

            await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(list, jsonOptions), token);
        }
        finally { gate.Release(); }
    }

    public async Task<List<MemoryEntry>> LoadAllAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try { return await ReadAllUnsafeAsync(token); }
        finally { gate.Release(); }
    }

    public async Task DeleteAsync(Guid id, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var list = await ReadAllUnsafeAsync(token);
            list.RemoveAll(m => m.Id == id);
            await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(list, jsonOptions), token);
        }
        finally { gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try { await File.WriteAllTextAsync(filePath, "[]", token); }
        finally { gate.Release(); }
    }

    private async Task<List<MemoryEntry>> ReadAllUnsafeAsync(CancellationToken token)
    {
        if (!File.Exists(filePath)) return new();
        var json = await File.ReadAllTextAsync(filePath, token);
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<MemoryEntry>>(json, jsonOptions) ?? new();
        }
        catch
        {
            var backup = filePath + ".bak_" + DateTimeOffset.Now.ToUnixTimeSeconds();
            try { File.Copy(filePath, backup, overwrite: false); } catch { }
            File.WriteAllText(filePath, "[]");
            return new();
        }
    }
}
