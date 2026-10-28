using System.Globalization;
using MudBlazor;
using SharpMUSH.Client.Components.Scenes;
using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Components.Map;

/// <summary>
/// How a legend entry looks on the map: its tone as a portal token, the same one <c>tone()</c> and a pose type's tone
/// use, and its icon as a MudBlazor icon by name or a character or two of text.
/// </summary>
public static class MapLegendStyle
{
	private static readonly IReadOnlyDictionary<string, string> IconSvgs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["water"] = Icons.Material.Outlined.Water,
		["forest"] = Icons.Material.Outlined.Forest,
		["park"] = Icons.Material.Outlined.Park,
		["mountain"] = Icons.Material.Outlined.Landscape,
		["road"] = Icons.Material.Outlined.AddRoad,
		["home"] = Icons.Material.Outlined.Home,
		["shop"] = Icons.Material.Outlined.Storefront,
		["food"] = Icons.Material.Outlined.Restaurant,
		["drink"] = Icons.Material.Outlined.LocalBar,
		["bed"] = Icons.Material.Outlined.Bed,
		["temple"] = Icons.Material.Outlined.Church,
		["castle"] = Icons.Material.Outlined.Castle,
		["shield"] = Icons.Material.Outlined.Shield,
		["anchor"] = Icons.Material.Outlined.Anchor,
		["star"] = Icons.Material.Outlined.StarOutline,
		["chat"] = Icons.Material.Outlined.ChatBubbleOutline,
		["book"] = Icons.Material.Outlined.MenuBook,
		["mask"] = Icons.Material.Outlined.TheaterComedy,
		["lock"] = Icons.Material.Outlined.Lock,
		["flag"] = Icons.Material.Outlined.Flag,
	};

	/// <summary>The icon names a legend may use, for the help and the builder.</summary>
	public static IEnumerable<string> IconNames => IconSvgs.Keys;

	/// <summary>The custom property a room of <paramref name="entry"/>'s kind carries, or null when it has no tone this portal knows.</summary>
	public static string? Style(MapLegendDto? entry) =>
		PoseTypeStyle.ToneToken(entry?.Tone) is { } token ? $"--room-tone: var({token});" : null;

	/// <summary>The MudBlazor icon an icon name shows, or null when it is not one.</summary>
	public static string? Svg(string? icon) =>
		!string.IsNullOrWhiteSpace(icon) && IconSvgs.TryGetValue(icon.Trim(), out var svg) ? svg : null;

	/// <summary>An icon that is no name: a character or two, drawn as text. Null for a longer value.</summary>
	public static string? Glyph(string? icon) =>
		!string.IsNullOrWhiteSpace(icon) && Svg(icon) is null && new StringInfo(icon.Trim()).LengthInTextElements <= 2
			? icon.Trim()
			: null;
}
