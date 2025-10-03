// SPDX-License-Identifier: MIT
// AiCompanionPlugin - ChatWindow.cs

#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;

namespace AiCompanionPlugin
{
    public sealed class ChatWindow
    {
        private readonly Configuration config;
        private readonly Func<IReadOnlyList<(string role, string content)>> getHistory;
        private readonly Action<string> sendUserMessage;

        private bool isOpen = true;
        private string inputBuffer = string.Empty;
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

        public bool IsOpen { get => isOpen; set => isOpen = value; }

        public void Draw()
        {
            if (!isOpen) return;

            if (!ImGui.Begin($"{config.AiDisplayName} — Chat ({config.ThemeName})", ref isOpen))
            {
                ImGui.End();
                return;
            }

            DrawHistorySection();
            ImGui.Separator();
            DrawInputSection();

            ImGui.End();
        }

        private void DrawHistorySection()
        {
            ImGui.TextDisabled("Conversation");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120f);
            ImGui.SliderFloat("##history_height", ref historyHeight, 200f, 700f, "Height: %.0f");

            if (ImGui.BeginChild("##chat_history_child", new Vector2(-1, historyHeight), true))
            {
                var hist = getHistory.Invoke();
                for (int i = 0; i < hist.Count; i++)
                {
                    var (role, content) = hist[i];
                    content ??= string.Empty;

                    ImGui.TextUnformatted(RoleLabel(role));
                    ImGui.SameLine();
                    ImGui.TextUnformatted("—");
                    ImGui.SameLine();

                    ImGui.PushTextWrapPos(0);
                    ImGui.TextUnformatted(content);
                    ImGui.PopTextWrapPos();

                    ImGui.Dummy(new Vector2(0, 4));
                }

                ImGui.EndChild();
            }
        }

        private void DrawInputSection()
        {
            ImGui.TextDisabled("Send Message");

            ImGui.SetNextItemWidth(-90f);
            if (ImGui.InputText("##chat_input", ref inputBuffer, 4096)) { }

            ImGui.SameLine();
            if (ImGui.Button("Send", new Vector2(80, 0)))
            {
                SubmitInput();
            }
        }

        private void SubmitInput()
        {
            var text = (inputBuffer ?? string.Empty).Trim();
            if (text.Length == 0) return;

            try { sendUserMessage.Invoke(text); }
            finally { inputBuffer = string.Empty; }
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
    }
}
