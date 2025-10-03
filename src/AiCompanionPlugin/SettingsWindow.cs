// SPDX-License-Identifier: MIT
// AiCompanionPlugin - SettingsWindow.cs

#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace AiCompanionPlugin
{
    /// <summary>
    /// Settings UI for AI Companion.
    /// Uses Dalamud.Bindings.ImGui.ImGui with UTF-8 label caching and byte-buffer InputText overloads.
    /// Avoids 'ref' on properties by staging to locals.
    /// </summary>
    public sealed class SettingsWindow
    {
        private readonly Configuration config;
        private readonly Action saveConfig;

        private bool isOpen = true;

        // Staged string values
        private string memoriesPath;
        private string chroniclePath;
        private string chronicleStyle;
        private string apiKeyBuffer;
        private string baseUrlBuffer;
        private string modelBuffer;

        // Byte buffers for InputText (ImGui byte-span overload)
        private byte[] bufMemories = new byte[1024];
        private byte[] bufChronPath = new byte[1024];
        private byte[] bufChronStyle = new byte[128];
        private byte[] bufApiKey = new byte[2048];
        private byte[] bufBaseUrl = new byte[1024];
        private byte[] bufModel = new byte[256];

        private string newSayWhitelist = string.Empty;
        private string newPartyWhitelist = string.Empty;
        private byte[] bufSayAdd = new byte[128];
        private byte[] bufPartyAdd = new byte[128];

        // ---------- UTF-8 label cache ----------
        private static readonly Dictionary<string, byte[]> LabelCache = new(StringComparer.Ordinal);

        private static ReadOnlySpan<byte> L(string s)
        {
            if (!LabelCache.TryGetValue(s, out var bytes))
            {
                // null-terminated, as ImGui expects
                var raw = Encoding.UTF8.GetBytes(s);
                bytes = new byte[raw.Length + 1];
                Buffer.BlockCopy(raw, 0, bytes, 0, raw.Length);
                bytes[bytes.Length - 1] = 0;
                LabelCache[s] = bytes;
            }
            return bytes;
        }

        public SettingsWindow(Configuration config, Action saveConfig)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.saveConfig = saveConfig ?? throw new ArgumentNullException(nameof(saveConfig));

            memoriesPath = config.MemoriesFileRelative ?? "Data/memories.json";
            chroniclePath = config.ChronicleFileRelative ?? "Data/chronicle.md";
            chronicleStyle = config.ChronicleStyle ?? "journal";
            apiKeyBuffer = config.ApiKey ?? string.Empty;
            baseUrlBuffer = config.BackendBaseUrl ?? string.Empty;
            modelBuffer = config.Model ?? string.Empty;
        }

        public bool IsOpen
        {
            get => isOpen;
            set => isOpen = value;
        }

        public void Draw()
        {
            if (!isOpen)
                return;

            if (!Dalamud.Bindings.ImGui.ImGui.Begin(
                    "AI Companion — Settings",
                    ref isOpen,
                    Dalamud.Bindings.ImGui.ImGuiWindowFlags.None))
            {
                Dalamud.Bindings.ImGui.ImGui.End();
                return;
            }

            if (Dalamud.Bindings.ImGui.ImGui.BeginTabBar(L("##ai_companion_tabs")))
            {
                if (Dalamud.Bindings.ImGui.ImGui.BeginTabItem(L("General")))
                {
                    DrawGeneral();
                    Dalamud.Bindings.ImGui.ImGui.EndTabItem();
                }

                if (Dalamud.Bindings.ImGui.ImGui.BeginTabItem(L("Backend")))
                {
                    DrawBackend();
                    Dalamud.Bindings.ImGui.ImGui.EndTabItem();
                }

                if (Dalamud.Bindings.ImGui.ImGui.BeginTabItem(L("Memory")))
                {
                    DrawMemory();
                    Dalamud.Bindings.ImGui.ImGui.EndTabItem();
                }

                if (Dalamud.Bindings.ImGui.ImGui.BeginTabItem(L("Chronicle")))
                {
                    DrawChronicle();
                    Dalamud.Bindings.ImGui.ImGui.EndTabItem();
                }

                if (Dalamud.Bindings.ImGui.ImGui.BeginTabItem(L("Routing")))
                {
                    DrawRouting();
                    Dalamud.Bindings.ImGui.ImGui.EndTabItem();
                }

                Dalamud.Bindings.ImGui.ImGui.EndTabBar();
            }

            Dalamud.Bindings.ImGui.ImGui.End();
        }

        // ===================== TABS =====================

        private void DrawGeneral()
        {
            Dalamud.Bindings.ImGui.ImGui.TextDisabled("Display");
            Dalamud.Bindings.ImGui.ImGui.Separator();

            // Display Name (stage to local to avoid ref-on-property)
            var dispName = config.AiDisplayName ?? "Nunu";
            TextInline("Name");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(300f);
            if (Utf8InputText(L("##ai_name"), ref dispName, ref bufModel, 256, allowEmpty: false))
            {
                config.AiDisplayName = dispName.Trim();
                saveConfig();
            }

            // Theme (stage local)
            var themeName = config.ThemeName ?? "Default";
            TextInline("Theme");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(300f);
            if (Utf8InputText(L("##ai_theme"), ref themeName, ref bufModel, 128, allowEmpty: false))
            {
                config.ThemeName = themeName.Trim();
                saveConfig();
            }

            Dalamud.Bindings.ImGui.ImGui.Spacing();
            Dalamud.Bindings.ImGui.ImGui.TextDisabled("Debug");
            Dalamud.Bindings.ImGui.ImGui.Separator();

            bool debugTap = config.DebugChatTap;
            if (Dalamud.Bindings.ImGui.ImGui.Checkbox(L("Enable Chat Tap Debug"), ref debugTap))
            {
                config.DebugChatTap = debugTap;
                saveConfig();
            }

            int debugLimit = config.DebugChatTapLimit;
            TextInline("Debug Tap Limit");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(120f);
            if (Dalamud.Bindings.ImGui.ImGui.InputInt(L("##dbg_limit"), ref debugLimit))
            {
                config.DebugChatTapLimit = Math.Max(1, debugLimit);
                saveConfig();
            }
        }

        private void DrawBackend()
        {
            Dalamud.Bindings.ImGui.ImGui.TextDisabled("Backend / Model");
            Dalamud.Bindings.ImGui.ImGui.Separator();

            // Base URL
            TextInline("Base URL");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(420f);
            if (Utf8InputText(L("##base_url"), ref baseUrlBuffer, ref bufBaseUrl, 1024, allowEmpty: true)) { }
            if (Dalamud.Bindings.ImGui.ImGui.Button(L("Apply Base URL")))
            {
                if (!string.IsNullOrWhiteSpace(baseUrlBuffer))
                    config.BackendBaseUrl = baseUrlBuffer.Trim();
                saveConfig();
            }

            // API Key
            TextInline("API Key");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(420f);
            if (Utf8InputText(L("##api_key"), ref apiKeyBuffer, ref bufApiKey, 2048, allowEmpty: true)) { }
            if (Dalamud.Bindings.ImGui.ImGui.Button(L("Apply API Key")))
            {
                config.ApiKey = apiKeyBuffer ?? string.Empty;
                saveConfig();
            }

            // Model
            TextInline("Model");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(340f);
            if (Utf8InputText(L("##model"), ref modelBuffer, ref bufModel, 256, allowEmpty: false)) { }
            if (Dalamud.Bindings.ImGui.ImGui.Button(L("Apply Model")))
            {
                if (!string.IsNullOrWhiteSpace(modelBuffer))
                    config.Model = modelBuffer.Trim();
                saveConfig();
            }

            // Temperature
            double temp = config.Temperature;
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(160f);
            if (Dalamud.Bindings.ImGui.ImGui.InputDouble(L("Temperature"), ref temp))
            {
                config.Temperature = Math.Clamp(temp, 0, 2);
                saveConfig();
            }

            // Max Tokens
            int maxTokens = config.MaxTokens;
            TextInline("Max Tokens (0 = auto)");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(140f);
            if (Dalamud.Bindings.ImGui.ImGui.InputInt(L("##max_tokens"), ref maxTokens))
            {
                config.MaxTokens = Math.Max(0, maxTokens);
                saveConfig();
            }

            // Timeout
            int timeout = config.RequestTimeoutSeconds;
            TextInline("Request Timeout (s)");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(140f);
            if (Dalamud.Bindings.ImGui.ImGui.InputInt(L("##timeout"), ref timeout))
            {
                config.RequestTimeoutSeconds = Math.Clamp(timeout, 5, 600);
                saveConfig();
            }

            // History size
            int hist = config.MaxHistoryMessages;
            TextInline("History Messages");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(140f);
            if (Dalamud.Bindings.ImGui.ImGui.InputInt(L("##hist_msgs"), ref hist))
            {
                config.MaxHistoryMessages = Math.Max(1, hist);
                saveConfig();
            }
        }

        private void DrawMemory()
        {
            Dalamud.Bindings.ImGui.ImGui.TextDisabled("Memory");
            Dalamud.Bindings.ImGui.ImGui.Separator();

            bool enable = config.EnableMemory;
            if (Dalamud.Bindings.ImGui.ImGui.Checkbox(L("Enable Memory"), ref enable))
            {
                config.EnableMemory = enable;
                saveConfig();
            }

            bool autosave = config.AutoSaveMemory;
            if (Dalamud.Bindings.ImGui.ImGui.Checkbox(L("Auto-Save Memory"), ref autosave))
            {
                config.AutoSaveMemory = autosave;
                saveConfig();
            }

            int maxMem = config.MaxMemories;
            TextInline("Max Memories");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(140f);
            if (Dalamud.Bindings.ImGui.ImGui.InputInt(L("##max_memories"), ref maxMem))
            {
                config.MaxMemories = Math.Max(0, maxMem);
                saveConfig();
            }

            bool asciiSafe = config.AsciiSafe;
            if (Dalamud.Bindings.ImGui.ImGui.Checkbox(L("ASCII-safe Output"), ref asciiSafe))
            {
                config.AsciiSafe = asciiSafe;
                saveConfig();
            }

            int maxLen = config.MaxPostLength;
            TextInline("Max Post Length");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(140f);
            if (Dalamud.Bindings.ImGui.ImGui.InputInt(L("##max_post_len"), ref maxLen))
            {
                config.MaxPostLength = Math.Clamp(maxLen, 50, 500);
                saveConfig();
            }

            // Memory file path
            TextInline("Memories File (relative)");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(420f);
            Utf8Sync(memoriesPath, ref bufMemories);
            bool changed = Dalamud.Bindings.ImGui.ImGui.InputText(L("##mem_file"), bufMemories.AsSpan(), Dalamud.Bindings.ImGui.ImGuiInputTextFlags.None);
            memoriesPath = Utf8ToString(bufMemories);
            if (Dalamud.Bindings.ImGui.ImGui.Button(L("Apply Memories Path")) || changed)
            {
                if (string.IsNullOrWhiteSpace(memoriesPath))
                    memoriesPath = "Data/memories.json";
                config.MemoriesFileRelative = memoriesPath.Trim();
                saveConfig();
            }
        }

        private void DrawChronicle()
        {
            Dalamud.Bindings.ImGui.ImGui.TextDisabled("Chronicle");
            Dalamud.Bindings.ImGui.ImGui.Separator();

            bool enable = config.EnableChronicle;
            if (Dalamud.Bindings.ImGui.ImGui.Checkbox(L("Enable Chronicle"), ref enable))
            {
                config.EnableChronicle = enable;
                saveConfig();
            }

            // Style
            TextInline("Style");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(200f);
            Utf8Sync(chronicleStyle, ref bufChronStyle);
            bool styleChanged = Dalamud.Bindings.ImGui.ImGui.InputText(L("##chron_style"), bufChronStyle.AsSpan(), Dalamud.Bindings.ImGui.ImGuiInputTextFlags.None);
            chronicleStyle = Utf8ToString(bufChronStyle);

            int maxEntries = config.ChronicleMaxEntries;
            TextInline("Max Entries");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(140f);
            if (Dalamud.Bindings.ImGui.ImGui.InputInt(L("##chron_max"), ref maxEntries))
            {
                config.ChronicleMaxEntries = Math.Max(1, maxEntries);
                saveConfig();
            }

            bool autoAppend = config.ChronicleAutoAppend;
            if (Dalamud.Bindings.ImGui.ImGui.Checkbox(L("Auto-Append"), ref autoAppend))
            {
                config.ChronicleAutoAppend = autoAppend;
                saveConfig();
            }

            // File path
            TextInline("Chronicle File (relative)");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(420f);
            Utf8Sync(chroniclePath, ref bufChronPath);
            bool pathChanged = Dalamud.Bindings.ImGui.ImGui.InputText(L("##chron_file"), bufChronPath.AsSpan(), Dalamud.Bindings.ImGui.ImGuiInputTextFlags.None);
            chroniclePath = Utf8ToString(bufChronPath);

            if (Dalamud.Bindings.ImGui.ImGui.Button(L("Apply Chronicle Settings")) || styleChanged || pathChanged)
            {
                config.ChronicleStyle = string.IsNullOrWhiteSpace(chronicleStyle) ? "journal" : chronicleStyle.Trim();
                if (string.IsNullOrWhiteSpace(chroniclePath))
                    chroniclePath = "Data/chronicle.md";
                config.ChronicleFileRelative = chroniclePath.Trim();
                saveConfig();
            }
        }

        private void DrawRouting()
        {
            Dalamud.Bindings.ImGui.ImGui.TextDisabled("Triggers");
            Dalamud.Bindings.ImGui.ImGui.Separator();

            // Say Trigger (stage local)
            var sayTrig = config.SayTrigger ?? string.Empty;
            TextInline("Say Trigger");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(200f);
            if (Utf8InputText(L("##say_trigger"), ref sayTrig, ref bufModel, 128, allowEmpty: true))
            {
                config.SayTrigger = sayTrig.Trim();
                saveConfig();
            }

            // Party Trigger (stage local)
            var partyTrig = config.PartyTrigger ?? string.Empty;
            TextInline("Party Trigger");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(200f);
            if (Utf8InputText(L("##party_trigger"), ref partyTrig, ref bufModel, 128, allowEmpty: true))
            {
                config.PartyTrigger = partyTrig.Trim();
                saveConfig();
            }

            bool require = config.RequireWhitelist;
            if (Dalamud.Bindings.ImGui.ImGui.Checkbox(L("Require Whitelist"), ref require))
            {
                config.RequireWhitelist = require;
                saveConfig();
            }

            Dalamud.Bindings.ImGui.ImGui.Spacing();
            Dalamud.Bindings.ImGui.ImGui.TextDisabled("Whitelist — /say");
            Dalamud.Bindings.ImGui.ImGui.Separator();

            TextInline("Add Name");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(220f);
            Utf8Sync(newSayWhitelist, ref bufSayAdd);
            bool sayAddChanged = Dalamud.Bindings.ImGui.ImGui.InputText(L("##say_add"), bufSayAdd.AsSpan(), Dalamud.Bindings.ImGui.ImGuiInputTextFlags.None);
            newSayWhitelist = Utf8ToString(bufSayAdd);
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            if (Dalamud.Bindings.ImGui.ImGui.Button(L("Add##say_wl")) ||
                (sayAddChanged && newSayWhitelist.Length > 0 && Dalamud.Bindings.ImGui.ImGui.IsItemDeactivatedAfterEdit()))
            {
                TryAddUnique(config.SayWhitelist, newSayWhitelist);
                newSayWhitelist = string.Empty;
                Array.Clear(bufSayAdd, 0, bufSayAdd.Length);
                saveConfig();
            }

            RenderList(config.SayWhitelist, "##say_wl_", saveConfig);

            Dalamud.Bindings.ImGui.ImGui.Spacing();
            Dalamud.Bindings.ImGui.ImGui.TextDisabled("Whitelist — /party");
            Dalamud.Bindings.ImGui.ImGui.Separator();

            TextInline("Add Name");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(220f);
            Utf8Sync(newPartyWhitelist, ref bufPartyAdd);
            bool partyAddChanged = Dalamud.Bindings.ImGui.ImGui.InputText(L("##party_add"), bufPartyAdd.AsSpan(), Dalamud.Bindings.ImGui.ImGuiInputTextFlags.None);
            newPartyWhitelist = Utf8ToString(bufPartyAdd);
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            if (Dalamud.Bindings.ImGui.ImGui.Button(L("Add##party_wl")) ||
                (partyAddChanged && newPartyWhitelist.Length > 0 && Dalamud.Bindings.ImGui.ImGui.IsItemDeactivatedAfterEdit()))
            {
                TryAddUnique(config.PartyWhitelist, newPartyWhitelist);
                newPartyWhitelist = string.Empty;
                Array.Clear(bufPartyAdd, 0, bufPartyAdd.Length);
                saveConfig();
            }

            RenderList(config.PartyWhitelist, "##party_wl_", saveConfig);
        }

        // ===================== HELPERS =====================

        private static void TextInline(string label)
            => Dalamud.Bindings.ImGui.ImGui.TextUnformatted(label);

        /// <summary>
        /// Byte-buffer InputText helper that edits a managed string value.
        /// </summary>
        private static bool Utf8InputText(ReadOnlySpan<byte> label, ref string value, ref byte[] scratch, int minCapacity, bool allowEmpty)
        {
            value ??= string.Empty;
            if (scratch is null || scratch.Length < minCapacity)
                Array.Resize(ref scratch, Math.Max(minCapacity, (scratch?.Length ?? 0) * 2 + 128));

            Utf8Sync(value, ref scratch);
            bool changed = Dalamud.Bindings.ImGui.ImGui.InputText(label, scratch.AsSpan(), Dalamud.Bindings.ImGui.ImGuiInputTextFlags.None);
            string next = Utf8ToString(scratch);

            if (!allowEmpty && string.IsNullOrWhiteSpace(next))
                next = value;

            bool isDifferent = !string.Equals(value, next, StringComparison.Ordinal);
            if (isDifferent)
                value = next;

            return changed || isDifferent;
        }

        private static void Utf8Sync(string src, ref byte[] dst)
        {
            src ??= string.Empty;
            int needed = Encoding.UTF8.GetByteCount(src) + 1;
            if (dst.Length < needed)
                Array.Resize(ref dst, Math.Max(needed, dst.Length * 2));

            int written = Encoding.UTF8.GetBytes(src, 0, src.Length, dst, 0);
            if (written < dst.Length) dst[written] = 0;
        }

        private static string Utf8ToString(byte[] buf)
        {
            if (buf is null || buf.Length == 0) return string.Empty;
            int len = Array.IndexOf(buf, (byte)0);
            if (len < 0) len = buf.Length;
            return Encoding.UTF8.GetString(buf, 0, len);
        }

        private static void TryAddUnique(List<string> list, string newValue)
        {
            var s = (newValue ?? string.Empty).Trim();
            if (s.Length == 0) return;

            foreach (var existing in list)
            {
                if (!string.IsNullOrWhiteSpace(existing) &&
                    string.Equals(existing.Trim(), s, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            list.Add(s);
        }

        private static void RenderList(List<string> list, string idPrefix, Action save)
        {
            if (list.Count == 0)
            {
                Dalamud.Bindings.ImGui.ImGui.TextDisabled("(empty)");
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i] ?? string.Empty;
                Dalamud.Bindings.ImGui.ImGui.BulletText(item);
                Dalamud.Bindings.ImGui.ImGui.SameLine();

                // Build and cache a UTF-8 label for the remove button
                var removeId = $"{idPrefix}Remove{i}";
                if (Dalamud.Bindings.ImGui.ImGui.SmallButton(L(removeId)))
                {
                    list.RemoveAt(i);
                    save();
                    break;
                }
            }
        }
    }
}
