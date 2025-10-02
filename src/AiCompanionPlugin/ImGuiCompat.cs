// Single-source the ImGui types from Dalamud.Bindings.ImGui so the rest of the code
// can just use ImGui / ImGuiCol / ImGuiWindowFlags / ImGuiInputTextFlags.

global using ImGui = Dalamud.Bindings.ImGui.ImGui;
global using ImGuiCol = Dalamud.Bindings.ImGui.ImGuiCol;
global using ImGuiWindowFlags = Dalamud.Bindings.ImGui.ImGuiWindowFlags;
global using ImGuiInputTextFlags = Dalamud.Bindings.ImGui.ImGuiInputTextFlags;
