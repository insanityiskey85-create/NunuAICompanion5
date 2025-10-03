using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin;

public sealed class ChatWindow : Window
{
    private readonly IPluginLog log;
    private readonly AiClient ai;
    private readonly Configuration cfg;
    private readonly PersonaManager persona;
    private readonly MemoryManager memory;
    private readonly ChronicleManager chron;
    private readonly ChatPipe pipe;

    private readonly List<string> incoming = new();
    private string compose = "";
    private string aiPreview = "";

    public ChatWindow(IPluginLog log, AiClient ai, Configuration cfg, PersonaManager persona, MemoryManager memory, ChronicleManager chron, ChatPipe pipe)
        : base("AI Companion", ImGuiWindowFlags.None)
    {
        this.log = log;
        this.ai = ai;
        this.cfg = cfg;
        this.persona = persona;
        this.memory = memory;
        this.chron = chron;
        this.pipe = pipe;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(650, 400),
            MaximumSize = new Vector2(4096, 4096)
        };
        RespectCloseHotkey = true;
        IsOpen = cfg.ShowChatWindowOnStart;
    }

    public override void Draw()
    {
        ImGui.TextColored(new Vector4(0.7f, 0.6f, 1.0f, 1f), $"{cfg.AiDisplayName} — {cfg.ThemeName}");

        // Split area: Left incoming, Right AI reply
        var avail = ImGui.GetContentRegionAvail();
        var half = new Vector2(avail.X / 2f - 6f, avail.Y - 90f);

        ImGui.BeginChild("incoming", half, true);
        ImGui.Text("Incoming (triggered party/say)");
        ImGui.Separator();
        foreach (var line in incoming)
            ImGui.TextWrapped(line);
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("reply", half, true);
        ImGui.Text("AI Nunu Reply");
        ImGui.Separator();
        ImGui.TextWrapped(aiPreview);
        ImGui.EndChild();

        ImGui.Separator();
        ImGui.InputTextWithHint("##compose", "Type a prompt for AI Nunu…", ref compose, 4096);
        ImGui.SameLine();
        if (ImGui.Button("Ask"))
        {
            ai.FakeReply(compose);
            aiPreview = ai.LastReply;
            incoming.Add($"You → {cfg.AiDisplayName}: {compose}");
            compose = string.Empty;
        }

        ImGui.SameLine();
        var canPost = !string.IsNullOrWhiteSpace(aiPreview);
        if (!canPost) ImGui.BeginDisabled();
        if (ImGui.Button("Post to /say"))
        {
            if (pipe.TrySendSay(aiPreview))
                log.Info("Posted AI reply to /say.");
        }
        ImGui.SameLine();
        if (ImGui.Button("Post to /party"))
        {
            if (pipe.TrySendParty(aiPreview))
                log.Info("Posted AI reply to /party.");
        }
        if (!canPost) ImGui.EndDisabled();
    }

    // optional helper so other systems can push lines here
    public void PushIncoming(string sender, string text, string channel)
    {
        incoming.Add($"[{channel}] {sender}: {text}");
    }
}
