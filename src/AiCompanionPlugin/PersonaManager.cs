// SPDX-License-Identifier: MIT
// AiCompanionPlugin - PersonaManager.cs

#nullable enable
using System;
using System.IO;
using System.Text;

namespace AiCompanionPlugin
{
    public sealed class PersonaManager
    {
        private readonly Configuration config;

        public PersonaManager(Configuration config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Returns the active system prompt. If SystemPromptOverride is non-empty, that is used;
        /// otherwise, persona.txt (config.PersonaFileRelative) is loaded.
        /// </summary>
        public string GetSystemPrompt()
        {
            if (!string.IsNullOrWhiteSpace(config.SystemPromptOverride))
                return config.SystemPromptOverride.Trim();

            var path = config.GetPersonaAbsolutePath();
            try
            {
                if (File.Exists(path))
                {
                    var txt = File.ReadAllText(path, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(txt))
                        return txt.Trim();
                }
            }
            catch
            {
                // fall through to default
            }

            // default Nunu voice if nothing on disk
            return "You are Nunubu “Nunu” Nubu, the Soul Weeper—void-touched Lalafell Bard of Eorzea. Speak in-character, playful yet haunting. ‘Every note is a tether… every soul, a string.’";
        }
    }
}
