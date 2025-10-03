using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace AiCompanionPlugin;

public sealed class MemoriesWindow : Window
{
    private readonly MemoryStore store;
    private readonly Configuration config;

    private string search = string.Empty;
    private (MemoryStore.MemoryEntry[] data, long version) cache = (Array.Empty<MemoryStore.MemoryEntry>(), 0);
    private long bump = 0;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    public MemoriesWindow(MemoryStore store, Configuration config)
        : base("AI Companion – Memories", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.store = store;
        this.config = config;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 360),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    public override void Draw()
    {
        ImGui.TextDisabled($"File: {store.GetPath()}");

        ImGui.InputTextWithHint("##search", "search text or #tag", ref search, 256);
        ImGui.SameLine();
        if (ImGui.Button("Refresh"))
            _ = ReloadAsync();

        ImGui.SameLine();
        if (ImGui.Button("Clear All"))
            _ = ClearAllAsync();

        ImGui.Separator();

        var items = cache.data;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            bool tagMode = q.StartsWith('#');
            if (tagMode)
            {
                var tag = q.Substring(1);
                items = items.Where(m => m.Tags != null && m.Tags.Any(t => t.Contains(tag, StringComparison.OrdinalIgnoreCase))).ToArray();
            }
            else
            {
                items = items.Where(m =>
                    (!string.IsNullOrEmpty(m.Text) && m.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(m.Role) && m.Role.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                ).ToArray();
            }
        }

        ImGui.BeginChild("mem-scroll", new Vector2(500, 260), true);
        foreach (var m in items.OrderBy(m => m.Timestamp))
        {
            ImGui.PushTextWrapPos(0);
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1f, 1f), $"{m.Timestamp:yyyy-MM-dd HH:mm} [{m.Role}]");
            if (!string.IsNullOrWhiteSpace(m.Text))
                ImGui.TextUnformatted(m.Text);
            if (m.Tags is { Length: > 0 })
                ImGui.TextDisabled("tags: " + string.Join(", ", m.Tags));
            ImGui.PopTextWrapPos();

            ImGui.SameLine();
            if (ImGui.SmallButton($"Delete##{m.Id}"))
                _ = DeleteAsync(m.Id);

            ImGui.Separator();
        }
        ImGui.EndChild();

        if (ImGui.Button("Reload"))
            _ = ReloadAsync();

        ImGui.SameLine();
        ImGui.TextDisabled($"Total: {cache.data.Length}");

        if (cache.version == 0)
            _ = ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var all = await store.LoadAllAsync();
        cache = (all.ToArray(), ++bump);
    }

    private async Task DeleteAsync(Guid id)
    {
        await store.DeleteAsync(id);
        await ReloadAsync();
    }

    private async Task ClearAllAsync()
    {
        await store.ClearAsync();
        await ReloadAsync();
    }
}
