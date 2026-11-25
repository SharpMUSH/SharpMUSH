using System.Text.Json.Serialization;

namespace SharpMUSH.Client.Models;

/// <summary>
/// One of the player's highlight rules for Play: a pattern matched against the plain text of each line the
/// server sends, and what happens to a line it matches. Kept on the account (<see cref="Services.HighlightRules"/>).
/// </summary>
/// <param name="Id">Stable within the list, so the editor can tell rules apart.</param>
/// <param name="Pattern">What to look for, read as <paramref name="Match"/> says.</param>
/// <param name="Match">How <paramref name="Pattern"/> is read.</param>
/// <param name="CaseSensitive">Letters must match in case; off, they match either way.</param>
/// <param name="Lines">Which lines the rule looks at, by the engine's own marking of them.</param>
/// <param name="Colour">The colour the line is marked with, or none.</param>
/// <param name="Sound">The sound played when a new line matches, or none.</param>
/// <param name="Notify">A new matching line raises a browser notification while the tab is not in front.</param>
/// <param name="Enabled">Off keeps the rule without using it.</param>
public sealed record HighlightRule(
	string Id,
	string Pattern,
	HighlightMatch Match = HighlightMatch.Words,
	bool CaseSensitive = false,
	HighlightLines Lines = HighlightLines.All,
	HighlightColour Colour = HighlightColour.Accent,
	HighlightSound Sound = HighlightSound.None,
	bool Notify = false,
	bool Enabled = true)
{
	/// <summary>The most rules one account keeps.</summary>
	public const int MaxRules = 50;

	/// <summary>The longest pattern a rule may have.</summary>
	public const int MaxPatternLength = 200;

	/// <summary>A new rule with a fresh id.</summary>
	public static HighlightRule Create(string pattern) => new(Guid.NewGuid().ToString("N")[..12], pattern);

	/// <summary>The rule does something to a line it matches.</summary>
	[JsonIgnore]
	public bool HasEffect => Colour != HighlightColour.None || Sound != HighlightSound.None || Notify;
}

/// <summary>How a rule's pattern is read.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HighlightMatch>))]
public enum HighlightMatch
{
	/// <summary>The words as typed, as whole words: <c>Ann</c> matches "Ann waves" but not "Anne".</summary>
	Words,

	/// <summary>A wildcard pattern over the whole line: <c>*</c> any run of characters, <c>?</c> one.</summary>
	Glob,

	/// <summary>A .NET regular expression, found anywhere in the line.</summary>
	Regex
}

/// <summary>Which lines a rule looks at.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HighlightLines>))]
public enum HighlightLines
{
	All,

	/// <summary>Pages only (the engine marks them).</summary>
	Pages,

	/// <summary>Channel lines only (the engine marks them).</summary>
	Channels,

	/// <summary>Everything that is neither a page nor a channel line: the room, emits, the game's replies.</summary>
	Other
}

/// <summary>
/// The colours a line can be marked with. Each is one of the theme's own colours, so a highlight reads in
/// every theme, light or dark.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<HighlightColour>))]
public enum HighlightColour
{
	None,
	Accent,
	Accent2,
	Accent3,
	Info,
	Success,
	Warn,
	Danger
}

/// <summary>The sounds a rule can play. Made in the browser (Web Audio), so they need no files.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HighlightSound>))]
public enum HighlightSound
{
	None,

	/// <summary>Two soft rising notes.</summary>
	Chime,

	/// <summary>One short high note.</summary>
	Ping,

	/// <summary>Two low taps.</summary>
	Knock
}
