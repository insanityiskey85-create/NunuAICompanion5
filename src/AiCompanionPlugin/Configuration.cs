// SPDX-License-Identifier: MIT
// AiCompanionPlugin - Configuration.cs

#nullable enable
using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.IO;

namespace AiCompanionPlugin
{
    /// <summary>
    /// Plugin configuration persisted by Dalamud.
    /// </summary>
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 6;

        // Injected by Initialize to allow Save()
        private IDalamudPluginInterface? pluginInterface;

        // =========================
        // Backend / Model
        // =========================
        public string BackendBaseUrl { get; set; } = "https://api.openai.com/v1/chat/completions";
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "gpt-4o-mini";
        public int MaxHistoryMessages { get; set; } = 20;
        public int MaxTokens { get; set; } = 0;
        public double Temperature { get; set; } = 0.7;
        public int RequestTimeoutSeconds { get; set; } = 60;

        // =========================
        // Display / Theme
        // =========================
        public string AiDisplayName { get; set; } = "Nunu";
        public string ThemeName { get; set; } = "Default";
        public bool OpenChatOnLoad { get; set; } = false;
        public bool OpenSettingsOnLoad { get; set; } = false;

        // =========================
        // Debug
        // =========================
        public bool DebugChatTap { get; set; } = false;
        public int DebugChatTapLimit { get; set; } = 100;

        // =========================
        // Auto-route triggers / filters
        // =========================
        public string SayTrigger { get; set; } = "@nunu";
        public string PartyTrigger { get; set; } = "@nunu";
        public bool RequireWhitelist { get; set; } = false;
        public List<string> SayWhitelist { get; set; } = new();
        public List<string> PartyWhitelist { get; set; } = new();

        // =========================
        // Outbound chat constraints
        // =========================
        public bool AsciiSafe { get; set; } = true;
        public int MaxPostLength { get; set; } = 450;

        // =========================
        // Memory
        // =========================
        public bool EnableMemory { get; set; } = true;
        public bool AutoSaveMemory { get; set; } = true;
        public int MaxMemories { get; set; } = 500;
        public string MemoriesFileRelative { get; set; } = "Data/memories.json";

        // =========================
        // Chronicle
        // =========================
        public bool EnableChronicle { get; set; } = false;
        public string ChronicleStyle { get; set; } = "journal";
        public int ChronicleMaxEntries { get; set; } = 1000;
        public bool ChronicleAutoAppend { get; set; } = true;
        public string ChronicleFileRelative { get; set; } = "Data/chronicle.md";

        // =========================
        // Persona
        // =========================
        /// <summary>Optional inline system prompt text to override persona file.</summary>
        public string SystemPromptOverride { get; set; } = string.Empty;

        /// <summary>Relative path to persona text file.</summary>
        public string PersonaFileRelative { get; set; } = "Data/persona.md";

        // =========================
        // Example lists
        // =========================
        public List<string> ExampleWhitelist { get; set; } = new();
        public List<string> ExampleBlacklist { get; set; } = new();

        public void Initialize(IDalamudPluginInterface pi)
        {
            pluginInterface = pi;
            MigrateIfNeeded();
            Save();
        }

        public void MigrateIfNeeded()
        {
            if (DebugChatTapLimit < 1) DebugChatTapLimit = 1;
            if (MaxMemories < 0) MaxMemories = 0;
            if (ChronicleMaxEntries < 1) ChronicleMaxEntries = 1;

            if (string.IsNullOrWhiteSpace(MemoriesFileRelative))
                MemoriesFileRelative = "Data/memories.json";

            if (string.IsNullOrWhiteSpace(ChronicleFileRelative))
                ChronicleFileRelative = "Data/chronicle.md";

            if (string.IsNullOrWhiteSpace(ChronicleStyle))
                ChronicleStyle = "journal";

            if (string.IsNullOrWhiteSpace(BackendBaseUrl))
                BackendBaseUrl = "https://api.openai.com/v1/chat/completions";

            if (string.IsNullOrWhiteSpace(Model))
                Model = "gpt-4o-mini";

            if (string.IsNullOrWhiteSpace(AiDisplayName))
                AiDisplayName = "Nunu";

            if (string.IsNullOrWhiteSpace(ThemeName))
                ThemeName = "Default";

            if (string.IsNullOrWhiteSpace(PersonaFileRelative))
                PersonaFileRelative = "Data/persona.md";

            if (MaxHistoryMessages < 1) MaxHistoryMessages = 1;
            if (RequestTimeoutSeconds < 5) RequestTimeoutSeconds = 5;
            if (Temperature < 0) Temperature = 0;
            if (Temperature > 2) Temperature = 2;
            if (MaxTokens < 0) MaxTokens = 0;

            if (MaxPostLength < 50) MaxPostLength = 50;
            if (MaxPostLength > 500) MaxPostLength = 500;

            SayWhitelist ??= new();
            PartyWhitelist ??= new();
            ExampleWhitelist ??= new();
            ExampleBlacklist ??= new();
            SystemPromptOverride ??= string.Empty;
        }

        public string GetAbsolutePath(IDalamudPluginInterface pi, string relative)
        {
            var baseDir = pi.ConfigDirectory?.FullName ?? AppContext.BaseDirectory;
            relative = string.IsNullOrWhiteSpace(relative) ? string.Empty : relative;
            return Path.GetFullPath(Path.Combine(baseDir, relative));
        }

        public string GetPersonaAbsolutePath(IDalamudPluginInterface pi)
            => GetAbsolutePath(pi, PersonaFileRelative);

        public void Save()
        {
            pluginInterface?.SavePluginConfig(this);
        }
    }
}
