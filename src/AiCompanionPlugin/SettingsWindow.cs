using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace AiCompanionPlugin;

public sealed class SettingsWindow : Window
{
    private readonly Configuration config;
    private readonly PersonaManager persona;

    public SettingsWindow(Configuration config, PersonaManager persona)
        : base("AI Companion Settings", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.config = config;
        this.persona = persona;
        RespectCloseHotkey = true;

        // Optional: size constraints for nicer layout
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 360),
            MaximumSize = new Vector2(4096, 4096)
        };
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("aic_tabs"))
            return;

        // ---- General ----
        if (ImGui.BeginTabItem("General"))
        {
            var aiName = config.AiDisplayName;
            if (ImGui.InputText("AI Name", ref aiName, 128))
                config.AiDisplayName = aiName;

            var theme = config.ThemeName;
            if (ImGui.InputText("Theme", ref theme, 128))
                config.ThemeName = theme;

            var ascii = config.UseAsciiOnlyNetwork;
            if (ImGui.Checkbox("ASCII-only network", ref ascii))
                config.UseAsciiOnlyNetwork = ascii;

            if (ImGui.Button("Save"))
                config.Save(Plugin.PluginInterface);

            ImGui.SameLine();
            if (ImGui.Button("Reload persona.txt"))
            {
                // if you have a real reload method, call it here.
                // persona.Reload();
            }

            ImGui.EndTabItem();
        }

        // ---- Model ----
        if (ImGui.BeginTabItem("Model"))
        {
            var baseUrl = config.ApiBaseUrl;
            if (ImGui.InputText("Base URL", ref baseUrl, 256))
                config.ApiBaseUrl = baseUrl;

            var model = config.Model;
            if (ImGui.InputText("Model", ref model, 256))
                config.Model = model;

            var provider = config.ModelProvider;
            if (ImGui.InputText("Provider", ref provider, 128))
                config.ModelProvider = provider;

            if (ImGui.Button("Save"))
                config.Save(Plugin.PluginInterface);

            ImGui.EndTabItem();
        }

        // ---- Memory ----
        if (ImGui.BeginTabItem("Memory"))
        {
            var enableMem = config.EnableMemory;
            if (ImGui.Checkbox("Enable Memory", ref enableMem))
                config.EnableMemory = enableMem;

            var autoSaveMem = config.AutoSaveMemory;
            if (ImGui.Checkbox("Auto Save Memory", ref autoSaveMem))
                config.AutoSaveMemory = autoSaveMem;

            var maxMems = config.MaxMemories;
            if (ImGui.InputInt("Max Memories", ref maxMems))
                config.MaxMemories = Math.Max(0, maxMems);

            var memFile = config.MemoriesFileRelative;
            if (ImGui.InputText("Memories File", ref memFile, 256))
                config.MemoriesFileRelative = memFile;

            if (ImGui.Button("Save"))
                config.Save(Plugin.PluginInterface);

            ImGui.EndTabItem();
        }

        // ---- Chronicle ----
        if (ImGui.BeginTabItem("Chronicle"))
        {
            var enableChron = config.EnableChronicle;
            if (ImGui.Checkbox("Enable Chronicle", ref enableChron))
                config.EnableChronicle = enableChron;

            var style = config.ChronicleStyle;
            if (ImGui.InputText("Style", ref style, 128))
                config.ChronicleStyle = style;

            var maxEntries = config.ChronicleMaxEntries;
            if (ImGui.InputInt("Max Entries", ref maxEntries))
                config.ChronicleMaxEntries = Math.Max(0, maxEntries);

            var autoAppend = config.ChronicleAutoAppend;
            if (ImGui.Checkbox("Auto Append", ref autoAppend))
                config.ChronicleAutoAppend = autoAppend;

            var chronFile = config.ChronicleFileRelative;
            if (ImGui.InputText("File", ref chronFile, 256))
                config.ChronicleFileRelative = chronFile;

            if (ImGui.Button("Save"))
                config.Save(Plugin.PluginInterface);

            ImGui.EndTabItem();
        }

        // ---- Chat Hooks ----
        if (ImGui.BeginTabItem("Chat Hooks"))
        {
            var enParty = config.EnablePartyListener;
            if (ImGui.Checkbox("Enable Party Listener", ref enParty))
                config.EnablePartyListener = enParty;

            ImGui.SameLine();

            var enSay = config.EnableSayListener;
            if (ImGui.Checkbox("Enable Say Listener", ref enSay))
                config.EnableSayListener = enSay;

            var partyTrigger = config.PartyTrigger;
            if (ImGui.InputText("Party Trigger", ref partyTrigger, 128))
                config.PartyTrigger = partyTrigger;

            var sayTrigger = config.SayTrigger;
            if (ImGui.InputText("Say Trigger", ref sayTrigger, 128))
                config.SayTrigger = sayTrigger;

            var reqWL = config.RequireWhitelist;
            if (ImGui.Checkbox("Require Whitelist", ref reqWL))
                config.RequireWhitelist = reqWL;

            ImGui.Separator();
            // Party WL
            var partyWLStr = string.Join(", ", config.PartyWhitelist);
            if (ImGui.InputText("Party Whitelist (comma-separated)", ref partyWLStr, 2048))
            {
                config.PartyWhitelist = new List<string>(
                    partyWLStr.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            }

            // Say WL
            var sayWLStr = string.Join(", ", config.SayWhitelist);
            if (ImGui.InputText("Say Whitelist (comma-separated)", ref sayWLStr, 2048))
            {
                config.SayWhitelist = new List<string>(
                    sayWLStr.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            }

            ImGui.Separator();
            var dbgTap = config.DebugChatTap;
            if (ImGui.Checkbox("Debug Chat Tap", ref dbgTap))
                config.DebugChatTap = dbgTap;

            var dbgLimit = config.DebugChatTapLimit;
            if (ImGui.InputInt("Debug Tap Limit", ref dbgLimit))
                config.DebugChatTapLimit = Math.Max(0, dbgLimit);

            if (ImGui.Button("Save Settings"))
                config.Save(Plugin.PluginInterface);

            ImGui.EndTabItem();
        }

        // ---- Persona ----
        if (ImGui.BeginTabItem("Persona"))
        {
            var text = persona.PersonaText;
            if (ImGui.InputTextMultiline("persona.txt", ref text, 16_384, new Vector2(600, 300)))
                persona.PersonaText = text;

            if (ImGui.Button("Save"))
                config.Save(Plugin.PluginInterface);

            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }
}
