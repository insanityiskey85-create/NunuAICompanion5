using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace AiCompanionPlugin;

public static class ThemePalette
{
	public sealed record Theme(string Name, List<(ImGuiCol Target, Vector4 Color)> Colors);

	public static readonly Dictionary<string, Theme> Presets = new()
	{
		["Eorzean Night"] = new Theme("Eorzean Night", new()
		{
			(ImGuiCol.WindowBg, RGBA(18,19,25,255)),
			(ImGuiCol.ChildBg,  RGBA(23,24,32,255)),
			(ImGuiCol.FrameBg,  RGBA(36,38,50,255)),
			(ImGuiCol.FrameBgHovered, RGBA(56,60,78,255)),
			(ImGuiCol.FrameBgActive,  RGBA(72,78,104,255)),
			(ImGuiCol.Border,   RGBA(60,62,74,255)),
			(ImGuiCol.Separator,RGBA(60,62,74,255)),
			(ImGuiCol.Text,     RGBA(220,224,234,255)),
			(ImGuiCol.Button,   RGBA(56,60,78,255)),
			(ImGuiCol.ButtonHovered, RGBA(80,86,118,255)),
			(ImGuiCol.ButtonActive,  RGBA(98,106,146,255)),
			(ImGuiCol.ScrollbarBg, RGBA(23,24,32,255)),
			(ImGuiCol.ScrollbarGrab, RGBA(60,62,74,255)),
			(ImGuiCol.ScrollbarGrabHovered, RGBA(80,86,118,255)),
			(ImGuiCol.ScrollbarGrabActive,  RGBA(98,106,146,255)),
			(ImGuiCol.CheckMark, RGBA(198,206,255,255)),
			(ImGuiCol.SliderGrab, RGBA(98,106,146,255)),
			(ImGuiCol.SliderGrabActive, RGBA(140,150,200,255)),
			(ImGuiCol.Header, RGBA(56,60,78,255)),
			(ImGuiCol.HeaderHovered, RGBA(80,86,118,255)),
			(ImGuiCol.HeaderActive, RGBA(98,106,146,255)),
			(ImGuiCol.Tab, RGBA(36,38,50,255)),
			(ImGuiCol.TabHovered, RGBA(80,86,118,255)),
			(ImGuiCol.TabActive, RGBA(72,78,104,255)),
		}),
		["Voidglass"] = new Theme("Voidglass", new()
		{
			(ImGuiCol.WindowBg, RGBA(8,8,10,245)),
			(ImGuiCol.ChildBg,  RGBA(16,16,24,245)),
			(ImGuiCol.FrameBg,  RGBA(24,24,36,255)),
			(ImGuiCol.Text,     RGBA(210,210,255,255)),
			(ImGuiCol.Border,   RGBA(38,38,60,255)),
			(ImGuiCol.Button,   RGBA(40,40,70,200)),
			(ImGuiCol.ButtonHovered, RGBA(70,70,120,220)),
			(ImGuiCol.ButtonActive,  RGBA(90,90,150,240)),
			(ImGuiCol.CheckMark, RGBA(180,170,255,255)),
			(ImGuiCol.Header, RGBA(40,40,70,200)),
			(ImGuiCol.HeaderHovered, RGBA(70,70,120,220)),
			(ImGuiCol.HeaderActive, RGBA(90,90,150,240)),
			(ImGuiCol.Tab, RGBA(24,24,36,255)),
			(ImGuiCol.TabHovered, RGBA(70,70,120,220)),
			(ImGuiCol.TabActive, RGBA(60,60,110,220)),
		}),
		["Maelstrom Red"] = new Theme("Maelstrom Red", new()
		{
			(ImGuiCol.WindowBg, RGBA(20,14,16,255)),
			(ImGuiCol.ChildBg,  RGBA(26,18,20,255)),
			(ImGuiCol.FrameBg,  RGBA(40,24,26,255)),
			(ImGuiCol.Text,     RGBA(240,220,220,255)),
			(ImGuiCol.Border,   RGBA(84,24,28,255)),
			(ImGuiCol.Button,   RGBA(120,24,28,220)),
			(ImGuiCol.ButtonHovered, RGBA(160,36,42,240)),
			(ImGuiCol.ButtonActive,  RGBA(196,48,56,255)),
			(ImGuiCol.Header, RGBA(120,24,28,220)),
			(ImGuiCol.HeaderHovered, RGBA(160,36,42,240)),
			(ImGuiCol.HeaderActive, RGBA(196,48,56,255)),
			(ImGuiCol.CheckMark, RGBA(255,200,200,255)),
		}),
		["Gridania Moss"] = new Theme("Gridania Moss", new()
		{
			(ImGuiCol.WindowBg, RGBA(14,18,14,255)),
			(ImGuiCol.ChildBg,  RGBA(18,22,18,255)),
			(ImGuiCol.FrameBg,  RGBA(28,36,28,255)),
			(ImGuiCol.Text,     RGBA(214,232,214,255)),
			(ImGuiCol.Border,   RGBA(52,76,52,255)),
			(ImGuiCol.Button,   RGBA(60,92,60,220)),
			(ImGuiCol.ButtonHovered, RGBA(84,126,84,235)),
			(ImGuiCol.ButtonActive,  RGBA(104,150,104,255)),
			(ImGuiCol.Header, RGBA(60,92,60,220)),
			(ImGuiCol.HeaderHovered, RGBA(84,126,84,235)),
			(ImGuiCol.HeaderActive, RGBA(104,150,104,255)),
		}),
		["Ul'dahn Ember"] = new Theme("Ul'dahn Ember", new()
		{
			(ImGuiCol.WindowBg, RGBA(24,18,10,255)),
			(ImGuiCol.ChildBg,  RGBA(30,22,12,255)),
			(ImGuiCol.FrameBg,  RGBA(48,34,18,255)),
			(ImGuiCol.Text,     RGBA(244,230,210,255)),
			(ImGuiCol.Border,   RGBA(90,70,30,255)),
			(ImGuiCol.Button,   RGBA(130,90,30,230)),
			(ImGuiCol.ButtonHovered, RGBA(170,120,40,245)),
			(ImGuiCol.ButtonActive,  RGBA(200,140,50,255)),
			(ImGuiCol.Header, RGBA(130,90,30,230)),
			(ImGuiCol.HeaderHovered, RGBA(170,120,40,245)),
			(ImGuiCol.HeaderActive, RGBA(200,140,50,255)),
		}),
	};

	public static int ApplyTheme(string name)
	{
		if (!Presets.TryGetValue(name, out var theme)) return 0;
		foreach (var (target, color) in theme.Colors)
			ImGui.PushStyleColor(target, color);
		return theme.Colors.Count;
	}

	public static void PopTheme(int count)
	{
		if (count > 0) ImGui.PopStyleColor(count);
	}

	private static Vector4 RGBA(byte r, byte g, byte b, byte a = 255)
		=> new(r / 255f, g / 255f, b / 255f, a / 255f);
}
