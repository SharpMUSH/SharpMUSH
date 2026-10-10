using MudBlazor;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Components.Scenes;

/// <summary>
/// How a pose type looks in the portal beyond its layout: its tone as a portal token, and its icon as a MudBlazor
/// icon. A story row carries the tone as <c>--pose-tone</c>, so a staff stylesheet can restyle one type by its
/// <c>data-pose-type</c> without the component writing a colour of its own.
/// </summary>
public static class PoseTypeStyle
{
	/// <summary>
	/// A tone's portal token: the same mapping as <c>shell.css</c>'s <c>.tone-*</c> classes, which <c>tone()</c>
	/// writes, so a type and its text agree in every theme.
	/// </summary>
	private static readonly IReadOnlyDictionary<string, string> ToneTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["foreground"] = "--text",
		["strong"] = "--text",
		["primary"] = "--accent",
		["secondary"] = "--special",
		["tertiary"] = "--tone-tertiary",
		["muted"] = "--text-dim",
		["subtle"] = "--text-faint",
		["link"] = "--accent",
		["success"] = "--success",
		["warning"] = "--warn",
		["error"] = "--danger",
		["info"] = "--info",
		["red"] = "--hue-red",
		["orange"] = "--hue-orange",
		["yellow"] = "--hue-yellow",
		["green"] = "--hue-green",
		["cyan"] = "--hue-cyan",
		["blue"] = "--hue-blue",
		["purple"] = "--hue-purple",
		["pink"] = "--hue-pink",
	};

	private static readonly IReadOnlyDictionary<string, string> IconSvgs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["radio"] = Icons.Material.Outlined.Radio,
		["phone"] = Icons.Material.Outlined.Phone,
		["chat"] = Icons.Material.Outlined.ChatBubbleOutline,
		["dice"] = Icons.Material.Outlined.Casino,
		["book"] = Icons.Material.Outlined.MenuBook,
		["scroll"] = Icons.Material.Outlined.HistoryEdu,
		["megaphone"] = Icons.Material.Outlined.Campaign,
		["eye"] = Icons.Material.Outlined.Visibility,
		["mask"] = Icons.Material.Outlined.TheaterComedy,
		["music"] = Icons.Material.Outlined.MusicNote,
		["star"] = Icons.Material.Outlined.StarOutline,
		["bolt"] = Icons.Material.Outlined.Bolt,
	};

	/// <summary>The token a tone names (<c>--text-dim</c>), or null for no tone or one this portal does not know.</summary>
	public static string? ToneToken(string? tone) =>
		!string.IsNullOrWhiteSpace(tone) && ToneTokens.TryGetValue(tone.Trim(), out var token) ? token : null;

	/// <summary>
	/// The custom properties a row of <paramref name="type"/> carries: <c>--pose-tone</c> when the type has a tone.
	/// Null when it has none, so the row takes the theme's own colours.
	/// </summary>
	public static string? Style(PoseTypeInfo type) =>
		ToneToken(type.Tone) is { } token ? $"--pose-tone: var({token});" : null;

	/// <summary>The icon an icon name shows, or null for none or one this portal does not know.</summary>
	public static string? Icon(string? icon) =>
		!string.IsNullOrWhiteSpace(icon) && IconSvgs.TryGetValue(icon.Trim(), out var svg) ? svg : null;
}
