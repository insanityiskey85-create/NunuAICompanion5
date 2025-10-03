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
    /// Simple in-plugin chat UI: shows history and lets the user send prompts.
    /// Uses Dalamud.Bindings.ImGui.* with the byte-buffer InputText overload (Span&lt;byte&gt;).
    /// </summary>
    public sealed class ChatWindow
    {
        private readonly Configuration config;
        private readonly Func<IReadOnlyList<(string role, string content)>> getHistory;
        private readonly Action<string> sendUserMessage;

        private bool isOpen = true;

        // Visible text value
        private string inputBuffer = string.Empty;

        // Backing UTF-8 buffer for ImGui InputText
        private byte[] inputBytes = new byte[4096];

        private float historyHeight = 360f;

        public ChatWindow(
            Configuration config,
            Func<IReadOnlyList<(string role, string content)>> getHistory,
            Action<string> sendUserMessage)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.getHistory = getHistory ?? throw new ArgumentNullException(nameof(getHistory));
            this.sendUserMessage = sendUserMessage ?? throw new ArgumentNullException(nameof(sendUserMessage));
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
                    $"{config.AiDisplayName} — Chat ({config.ThemeName})",
                    ref isOpen,
                    Dalamud.Bindings.ImGui.ImGuiWindowFlags.None))
            {
                Dalamud.Bindings.ImGui.ImGui.End();
                return;
            }

            DrawHistorySection();
            Dalamud.Bindings.ImGui.ImGui.Separator();
            DrawInputSection();

            Dalamud.Bindings.ImGui.ImGui.End();
        }

        private void DrawHistorySection()
        {
            Dalamud.Bindings.ImGui.ImGui.TextDisabled("Conversation");
            Dalamud.Bindings.ImGui.ImGui.SameLine();
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(120f);
            Dalamud.Bindings.ImGui.ImGui.SliderFloat("##history_height", ref historyHeight, 200f, 800f, "Height: %.0f");

            if (Dalamud.Bindings.ImGui.ImGui.BeginChild(
                    "##chat_history_child",
                    new Vector2(-1, historyHeight),
                    true,
                    Dalamud.Bindings.ImGui.ImGuiWindowFlags.None))
            {
                var hist = getHistory.Invoke();

                for (int i = 0; i < hist.Count; i++)
                {
                    var (role, content) = hist[i];
                    content ??= string.Empty;

                    Dalamud.Bindings.ImGui.ImGui.TextUnformatted(RoleLabel(role));
                    Dalamud.Bindings.ImGui.ImGui.SameLine();
                    Dalamud.Bindings.ImGui.ImGui.TextUnformatted("—");
                    Dalamud.Bindings.ImGui.ImGui.SameLine();

                    Dalamud.Bindings.ImGui.ImGui.PushTextWrapPos(0);
                    Dalamud.Bindings.ImGui.ImGui.TextUnformatted(content);
                    Dalamud.Bindings.ImGui.ImGui.PopTextWrapPos();

                    Dalamud.Bindings.ImGui.ImGui.Dummy(new Vector2(0, 4));
                }

                Dalamud.Bindings.ImGui.ImGui.EndChild();
            }
        }

        private void DrawInputSection()
        {
            Dalamud.Bindings.ImGui.ImGui.TextDisabled("Send Message");
            Dalamud.Bindings.ImGui.ImGui.SetNextItemWidth(-90f);

            // Sync managed string -> UTF-8 byte buffer (null-terminated)
            SyncStringToBytes(inputBuffer, ref inputBytes);

            // Byte-buffer overload: label is UTF-8 literal; buffer is Span<byte>; flags handle Enter.
            bool submittedByEnter = Dalamud.Bindings.ImGui.ImGui.InputText(
                "##chat_input"u8,
                inputBytes.AsSpan(),
                Dalamud.Bindings.ImGui.ImGuiInputTextFlags.EnterReturnsTrue);

            // Sync back UTF-8 bytes -> managed string
            inputBuffer = BytesToString(inputBytes);

            if (submittedByEnter)
            {
                SubmitInput();
                // Clear buffer for next line
                inputBuffer = string.Empty;
                Array.Clear(inputBytes, 0, inputBytes.Length);
            }

            Dalamud.Bindings.ImGui.ImGui.SameLine();
            if (Dalamud.Bindings.ImGui.ImGui.Button("Send", new Vector2(80, 0)))
            {
                SubmitInput();
                inputBuffer = string.Empty;
                Array.Clear(inputBytes, 0, inputBytes.Length);
            }
        }

        private void SubmitInput()
        {
            var text = (inputBuffer ?? string.Empty).Trim();
            if (text.Length == 0)
                return;

            try
            {
                sendUserMessage.Invoke(text);
            }
            catch
            {
                // swallow UI exceptions
            }
        }

        private static string RoleLabel(string role)
        {
            role = (role ?? string.Empty).Trim().ToLowerInvariant();
            return role switch
            {
                "system" => "[System]",
                "assistant" => "[AI]",
                "user" => "[You]",
                _ => $"[{role}]"
            };
        }

        // -------- UTF-8 helpers for the byte-buffer InputText --------

        private static void SyncStringToBytes(string src, ref byte[] dst)
        {
            src ??= string.Empty;
            // Ensure capacity (room for null terminator)
            int needed = Encoding.UTF8.GetByteCount(src) + 1;
            if (dst.Length < needed)
                Array.Resize(ref dst, Math.Max(needed, dst.Length * 2));

            int written = Encoding.UTF8.GetBytes(src, 0, src.Length, dst, 0);
            if (written < dst.Length) dst[written] = 0; // null-terminate
        }

        private static string BytesToString(byte[] buf)
        {
            if (buf is null || buf.Length == 0) return string.Empty;
            int len = Array.IndexOf(buf, (byte)0);
            if (len < 0) len = buf.Length; // no null found; use full buffer
            return Encoding.UTF8.GetString(buf, 0, len);
        }
    }
}
