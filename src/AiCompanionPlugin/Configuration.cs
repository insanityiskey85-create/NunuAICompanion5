// SPDX-License-Identifier: MIT
// AiCompanionPlugin - Configuration.cs

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace AiCompanionPlugin
{
    public enum ChatPostingMode
    {
        Auto = 0,       // Try ChatTwo IPC, then fall back to Native
        ChatTwoIpc = 1, // Force ChatTwo IPC; if it fails, do not fall back
        Native = 2      // Force native /say and /p via ICommandManager
    }

    [Serializable]
    public sealed class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;

        // --- UI ---
        public string AiDisplayName { get; set; } = "Nunu";
        public string ThemeName { get; set; } = "Default";
        public bool OpenChatOnLoad { get; set; } = true;
        public bool OpenSettingsOnLoad { get; set; } = false;

        // --- Backend ---
        public string ApiKey { get; set; } = "";
        public string BackendBaseUrl { get; set; } = "";
        public string Model { get; set; } = "gpt-4o-mini";
        public double Temperature { get; set; } = 0.7;
        public int MaxTokens { get; set; } = 0; // 0 = auto
        public int RequestTimeoutSeconds { get; set; } = 60;
        public int MaxHistoryMessages { get; set; } = 20;

        // --- Memory / Chronicle ---
        public bool EnableMemory { get; set; } = true;
        public bool AutoSaveMemory { get; set; } = true;
        public int MaxMemories { get; set; } = 250;
        public string MemoriesFileRelative { get; set; } = "Data/memories.json";

        public bool EnableChronicle { get; set; } = true;
        public string ChronicleStyle { get; set; } = "journal";
        public int ChronicleMaxEntries { get; set; } = 500;
        public bool ChronicleAutoAppend { get; set; } = true;
        public string ChronicleFileRelative { get; set; } = "Data/chronicle.md";

        // --- Output shaping ---
        public bool AsciiSafe { get; set; } = false;
        public int MaxPostLength { get; set; } = 300;

        // --- Triggers / Routing ---
        public string SayTrigger { get; set; } = "!nunu";
        public string PartyTrigger { get; set; } = "!nunu";
        public bool RequireWhitelist { get; set; } = false;
        public List<string> SayWhitelist { get; set; } = new();
        public List<string> PartyWhitelist { get; set; } = new();

        // --- Persona / System prompt ---
        public string SystemPromptOverride { get; set; } = "";
        public string PersonaFileRelative { get; set; } = "persona.txt";

        // --- Debug ---
        public bool DebugChatTap { get; set; } = false;
        public int DebugChatTapLimit { get; set; } = 200;

        // --- ChatWindow behavior ---
        public bool PostAssistantToSay { get; set; } = false;
        public bool PostAssistantToParty { get; set; } = false;

        // --- Chat posting route ---
        public ChatPostingMode ChatPostingMode { get; set; } = ChatPostingMode.ChatTwoIpc;

        [NonSerialized] private IDalamudPluginInterface? pluginInterface;

        public void Initialize(IDalamudPluginInterface pi)
        {
            pluginInterface = pi;
            EnsureDataDirs();
        }

        public void Save()
        {
            pluginInterface?.SavePluginConfig(this);
        }

        public string GetAbsolutePath(string relative)
        {
            if (pluginInterface == null) return Path.GetFullPath(relative);
            var root = pluginInterface.ConfigDirectory.FullName;
            var path = Path.Combine(root, relative ?? "");
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return path;
        }

        public string GetPersonaAbsolutePath() => GetAbsolutePath(PersonaFileRelative);
        public string GetChronicleAbsolutePath() => GetAbsolutePath(ChronicleFileRelative);
        public string GetMemoriesAbsolutePath() => GetAbsolutePath(MemoriesFileRelative);

        private void EnsureDataDirs()
        {
            _ = GetMemoriesAbsolutePath();
            _ = GetChronicleAbsolutePath();
            _ = GetPersonaAbsolutePath();
        }
    }
}
