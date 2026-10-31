using System.Globalization;
using SharpMUSH.Client.Components.Scenes;
using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Components.Map;

/// <summary>
/// How a legend entry looks on the map: its tone as a portal token, the same one <c>tone()</c> and a pose type's tone
/// use, and its icon as a map icon by name (<see cref="MapIcons"/>) or a character or two of text.
/// </summary>
public static class MapLegendStyle
{
	/// <summary>The <c>tone()</c> colour names a legend may use, for the builder.</summary>
	public static IEnumerable<string> ToneNames => PoseTypeStyle.ToneNames;

	/// <summary>The icon names a legend may use, for the help and the builder.</summary>
	public static IEnumerable<string> IconNames => MapIcons.All.Keys;

	/// <summary>The custom property a room of <paramref name="entry"/>'s kind carries, or null when it has no tone this portal knows.</summary>
	public static string? Style(MapLegendDto? entry) => Style(entry?.Tone);

	/// <summary>The custom property a <c>tone()</c> colour's name gives, or null when this portal does not know it.</summary>
	public static string? Style(string? tone) =>
		PoseTypeStyle.ToneToken(tone) is { } token ? $"--room-tone: var({token});" : null;

	/// <summary>The map icon an icon name shows, as MudBlazor icon markup, or null when it is not one.</summary>
	public static string? Svg(string? icon) =>
		!string.IsNullOrWhiteSpace(icon) && MapIcons.All.TryGetValue(icon.Trim(), out var found) ? found.Svg : null;

	/// <summary>Who drew the map icons <paramref name="icons"/> name, each once, for the credit the icons' licence asks for.</summary>
	public static IReadOnlyList<string> Authors(IEnumerable<string?> icons) =>
		[.. icons.Select(icon => !string.IsNullOrWhiteSpace(icon) && MapIcons.All.TryGetValue(icon.Trim(), out var found) ? found.Author : null)
			.OfType<string>().Distinct().Order(StringComparer.Ordinal)];

	/// <summary>An icon that is no name: a character or two, drawn as text. Null for a longer value.</summary>
	public static string? Glyph(string? icon) =>
		!string.IsNullOrWhiteSpace(icon) && Svg(icon) is null && new StringInfo(icon.Trim()).LengthInTextElements <= 2
			? icon.Trim()
			: null;
}
