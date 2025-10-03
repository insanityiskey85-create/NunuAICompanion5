// SPDX-License-Identifier: MIT
// AiCompanionPlugin - PersonaManager.cs

#nullable enable
using System;
using System.IO;
using System.Text;
using Dalamud.Plugin;

namespace AiCompanionPlugin
{
    /// <summary>
    /// Loads the persona/system prompt from either inline override or a file relative to the plugin config dir.
    /// </summary>
    public sealed class PersonaManager
    {
        private readonly Configuration config;
        private readonly IDalamudPluginInterface pi;

        public PersonaManager(Configuration config, IDalamudPluginInterface pi)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.pi = pi ?? throw new ArgumentNullException(nameof(pi));
        }

        public string GetSystemPrompt()
        {
            var inline = config.SystemPromptOverride?.Trim();
            if (!string.IsNullOrEmpty(inline))
                return inline;

            var path = config.GetPersonaAbsolutePath(pi);
            try
            {
                return File.Exists(path)
                    ? File.ReadAllText(path, new UTF8Encoding(false))
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
