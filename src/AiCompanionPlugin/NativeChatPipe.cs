// SPDX-License-Identifier: MIT
// AiCompanionPlugin - NativeChatPipe.cs

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Dalamud.Plugin.Services;

namespace AiCompanionPlugin
{
    public sealed class NativeChatPipe
    {
        private readonly Configuration config;
        private readonly IChatGui chat;
        private readonly MethodInfo? sendMessageMethod;

        public NativeChatPipe(Configuration config, IChatGui chat)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.chat = chat ?? throw new ArgumentNullException(nameof(chat));

            try
            {
                sendMessageMethod = chat.GetType().GetMethod(
                    "SendMessage",
                    BindingFlags.Instance | BindingFlags.Public,
                    binder: null,
                    types: new[] { typeof(string) },
                    modifiers: null
                );
            }
            catch { sendMessageMethod = null; }
        }

        public bool SendSay(string text) => SendWithPrefix("/say ", text);
        public bool SendParty(string text) => SendWithPrefix("/p ", text);
        public bool SendRaw(string line)
        {
            var ok = true;
            foreach (var chunk in PrepareOutbound(line ?? string.Empty))
                ok &= TrySend(chunk);
            return ok;
        }

        private bool SendWithPrefix(string prefix, string text)
        {
            var ok = true;
            foreach (var chunk in PrepareOutbound(text ?? string.Empty))
                ok &= TrySend(prefix + chunk);
            return ok;
        }

        private bool TrySend(string line)
        {
            try
            {
                if (sendMessageMethod is not null)
                {
                    sendMessageMethod.Invoke(chat, new object?[] { line ?? string.Empty });
                    return true;
                }

                chat.Print(line ?? string.Empty);
                return true;
            }
            catch { return false; }
        }

        // -------- text prep (ASCII + chunk) --------

        private IReadOnlyList<string> PrepareOutbound(string text)
        {
            text = (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
            if (config.AsciiSafe) text = ToAsciiSafe(text);
            text = text.Trim();

            int maxLen = Math.Clamp(config.MaxPostLength, 50, 500);
            return ChunkText(text, maxLen);
        }

        private static string ToAsciiSafe(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            var normalized = input.Normalize(NormalizationForm.FormKD);
            var sb = new StringBuilder(normalized.Length);

            foreach (var rune in normalized.EnumerateRunes())
            {
                if (IsAsciiPrintable(rune)) { sb.Append(rune.ToString()); continue; }
                switch (rune.Value)
                {
                    case 0x2018:
                    case 0x2019:
                    case 0x2032: sb.Append('\''); break;
                    case 0x201C:
                    case 0x201D:
                    case 0x2033: sb.Append('"'); break;
                    case 0x2013:
                    case 0x2014: sb.Append('-'); break;
                    case 0x2026: sb.Append("..."); break;
                    case 0x00A0: sb.Append(' '); break;
                    default:
                        if (!Rune.GetUnicodeCategory(rune).HasFlag(System.Globalization.UnicodeCategory.NonSpacingMark))
                        {
                            // drop others
                        }
                        break;
                }
            }
            return CollapseSpaces(sb.ToString());
        }

        private static bool IsAsciiPrintable(Rune r) => r.Value == '\n' || r.Value == '\t' || (r.Value >= 0x20 && r.Value <= 0x7E);

        private static string CollapseSpaces(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            bool last = false;
            foreach (char c in s)
            {
                if (c == ' ')
                {
                    if (!last) { sb.Append(c); last = true; }
                }
                else { sb.Append(c); last = false; }
            }
            return sb.ToString();
        }

        private static List<string> ChunkText(string text, int maxLen)
        {
            var chunks = new List<string>();
            if (string.IsNullOrEmpty(text)) { chunks.Add(string.Empty); return chunks; }

            var lines = text.Split('\n');
            var current = new StringBuilder(maxLen);

            void Flush()
            {
                if (current.Length > 0) { chunks.Add(current.ToString().Trim()); current.Clear(); }
            }

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) { Flush(); continue; }

                foreach (var word in SplitPreservingLongTokens(line, maxLen))
                {
                    if (word.Length == 0) continue;

                    if (current.Length == 0)
                    {
                        current.Append(word.Length <= maxLen ? word : word.Substring(0, Math.Min(word.Length, maxLen)));
                        continue;
                    }

                    int needed = 1 + word.Length;
                    if (current.Length + needed <= maxLen) current.Append(' ').Append(word);
                    else { Flush(); current.Append(word); }
                }
            }

            Flush();
            for (int i = chunks.Count - 1; i >= 0; i--) if (string.IsNullOrWhiteSpace(chunks[i])) chunks.RemoveAt(i);
            if (chunks.Count == 0) chunks.Add(string.Empty);
            return chunks;
        }

        private static IEnumerable<string> SplitPreservingLongTokens(string line, int maxLen)
        {
            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var t in tokens)
            {
                if (t.Length <= maxLen) { yield return t; continue; }
                foreach (var slice in SliceByRuneCount(t, maxLen)) yield return slice;
            }
        }

        private static IEnumerable<string> SliceByRuneCount(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) yield break;
            var sb = new StringBuilder(maxLen);
            int cur = 0;
            foreach (var rune in s.EnumerateRunes())
            {
                var repr = rune.ToString();
                int add = repr.Length;
                if (cur + add > maxLen) { yield return sb.ToString(); sb.Clear(); cur = 0; }
                sb.Append(repr); cur += add;
            }
            if (sb.Length > 0) yield return sb.ToString();
        }
    }
}
