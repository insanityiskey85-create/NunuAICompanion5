// SPDX-License-Identifier: MIT
// AiCompanionPlugin - ChatWindow.cs

#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace AiCompanionPlugin
{
    /// <summary>
    /// Simple chat UI for Nunu.
    /// - Shows history
    /// - Input box with Send
    /// - Buttons to send current input directly to /say or /party
    /// - Checkboxes to also post assistant replies to /say or /party
    /// Uses Dalamud.Bindings.ImGui.ImGui with UTF-8 label cache and byte-buffer inputs.
    /// </summary>
    public sealed class ChatWindow
    {
        private readonly Configuration config;
        private readonly Func<IReadOnlyList<(string role, string content)>> getHistory;
        private readonly Action<string> sendUserMessage; // calls back into plugin (which will generate reply)

        // Optional sender to chat channels (provided by Plugin)
        private readonly Func<int /*XivChatType*/, string, bool>? trySendChannel; // 1 = Say, 2 = Party (tiny shim, see Plugin)

        private bool isOpen = false;

        private string input = string.Empty;
        private byte[] bufInput = new byte[1024];

        private static readonly Dictionary<string, byte[]> LabelCache = new(StringComparer.Ordinal);
        private static ReadOnlySpan<byte> L(string s)
        {
            if (!LabelCache.TryGetValue(s, out var bytes))
            {
                var raw = Encoding.UTF8.GetBytes(s);
                bytes = new byte[raw.Length + 1];
                Buffer.BlockCopy(raw, 0, bytes, 0, raw.Length);
                bytes[^1] = 0;
                LabelCache[s] = bytes;
            }
            return bytes;
        }

        public ChatWindow(
            Configuration config,
            Func<IReadOnlyList<(string role, string content)>> getHistory,
            Action<string> sendUserMessage,
            Func<int, string, bool>? trySendChannel = null)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.getHistory = getHistory ?? throw new ArgumentNullException(nameof(getHistory));
            this.sendUserMessage = sendUserMessage ?? throw new ArgumentNullException(nameof(sendUserMessage));
            this.trySendChannel = trySendChannel;
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

            if (!Dalamud.Bindings.ImGui.ImGui.Begin("AI Companion — Chat", ref isOpen, Dalamud.Bindings.ImGui.ImGuiWindowFlags.None))
            {
                Dalamud.Bindings.ImGui.ImGui.End();
                return;
            }

            var history = getHistory();

            // Header
            Dalamud.Bindings.ImGui.ImGui.TextDisabled($"Bard: {config.AiDisplayName}");
            Dalamud.Bindings.ImGui.ImGui.Separator();

            // History area
            var avail = Dalamud.Bindings.ImGui.ImGui.GetContentRegionAvail();
            var historyHeight = Math.Max(100f, avail.Y - 110f);

            if (Dalamud.Bindings.ImGui.ImGui.BeginChild(L("##chat_scroll"), new Vector2(0, historyHeight), false, Dalamud.Bindings.ImGui.ImGuiWindowFlags.HorizontalScrollbar))
            {
                foreach (var (role, content) in history)
                {
                    var who = role switch
                    {
                        "user" => "You",
                        "assistant" => config.AiDisplayName ?? "Nunu",
                        _ => role
                    };
                    Dalamud.Bindings.ImGui.ImGui.TextDisabled(who);
                    Dalamud.Bindings.ImGui.ImGui.SameLine();
                    Dalamud.Bindings.ImGui.ImGui.TextUnformatted(": ");
                    Dalamud.Bindings.ImGui.ImGui.SameLine();
                    Dalamud.Bindings.ImGui.ImGui.PushTextWrapPos(0);
                    Dalamud.Bindings.ImGui.ImGui.TextUnformatted(content ?? string.Empty);
                    Dalamud.Bindings.ImGui.ImGui.PopTextWrapPos();
                }
                Dalamud.Bindings.ImGui.ImGui.EndChild();
            }

            // Footer: reply posting options
            bool postSay = config.PostAssistantToSay;
            if (Dalamud.Bindings.ImGui.ImGui.Checkbox(L("Also post assistant replies to /say"), ref postSay))
            {
                config.PostAssistantToSay = postSay;
            }
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            bool postParty = config.PostAssistantToParty;
            if (Dalamud.Bindings.ImGui.ImGui.Checkbox(L("Also post assistant replies to /party"), ref postParty))
            {
                config.PostAssistantToParty = postParty;
            }

            // Input row
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(-120f);
            Utf8Sync(input, ref bufInput);
            Dalamud.Bindings.ImGui.ImGui.InputText(L("##chat_input"), bufInput.AsSpan(), Dalamud.Bindings.ImGui.ImGuiInputTextFlags.None);
            input = Utf8ToString(bufInput);

            Dalamud.Bindings.ImGui.ImGui.SameLine();
            if (Dalamud.Bindings.ImGui.ImGui.Button(L("Send")))
            {
                var msg = (input ?? string.Empty).Trim();
                if (msg.Length > 0)
                {
                    sendUserMessage(msg);
                    input = string.Empty;
                    Array.Clear(bufInput, 0, bufInput.Length);
                }
            }

            // Quick send buttons to chat channels (uses provided trySendChannel if present)
            if (trySendChannel is not null)
            {
                Dalamud.Bindings.ImGui.ImGui.SameLine();
                if (Dalamud.Bindings.ImGui.ImGui.Button(L("Say")))
                {
                    var s = (input ?? string.Empty).Trim();
                    if (s.Length > 0)
                        trySendChannel.Invoke(1, s); // 1 => Say
                }

                Dalamud.Bindings.ImGui.ImGui.SameLine();
                if (Dalamud.Bindings.ImGui.ImGui.Button(L("Party")))
                {
                    var s = (input ?? string.Empty).Trim();
                    if (s.Length > 0)
                        trySendChannel.Invoke(2, s); // 2 => Party
                }
            }

            Dalamud.Bindings.ImGui.ImGui.End();
        }

        // -------- helpers --------
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
    }
}
