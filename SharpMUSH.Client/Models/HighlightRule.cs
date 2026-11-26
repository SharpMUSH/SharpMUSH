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
/// <param name="Target">What <paramref name="Colour"/>, <paramref name="Style"/> and <paramref name="Rewrite"/> apply to: the
/// whole line, every place the pattern matches in it, or one group of the pattern there.</param>
/// <param name="Group">The group <paramref name="Target"/> names: a number (a wildcard numbers each <c>*</c> and
/// <c>?</c> from 1, a regex its parentheses) or a regex group's name.</param>
/// <param name="Paint">Whether <paramref name="Colour"/> tints behind the text or colours the text itself.</param>
/// <param name="Style">Bold, italic, underline, strike-through.</param>
/// <param name="Hide">A matching line is not shown. Its sound and notification still happen, and the log keeps it.</param>
/// <param name="Rewrite">Text shown in place of the target, with <c>$1</c>, <c>${name}</c> and <c>$0</c> for what matched; none to show it as sent.</param>
/// <param name="Stop">Rules after this one are not tried on a line it matches.</param>
public sealed record HighlightRule(
	string Id,
	string Pattern,
	HighlightMatch Match = HighlightMatch.Words,
	bool CaseSensitive = false,
	HighlightLines Lines = HighlightLines.All,
	HighlightColour Colour = HighlightColour.Accent,
	HighlightSound Sound = HighlightSound.None,
	bool Notify = false,
	bool Enabled = true,
	HighlightTarget Target = HighlightTarget.Line,
	string? Group = null,
	HighlightPaint Paint = HighlightPaint.Tint,
	HighlightStyle Style = HighlightStyle.None,
	bool Hide = false,
	string? Rewrite = null,
	bool Stop = false)
{
	/// <summary>The most rules one account keeps.</summary>
	public const int MaxRules = 50;

	/// <summary>The longest pattern a rule may have.</summary>
	public const int MaxPatternLength = 200;

	/// <summary>The longest rewrite a rule may have.</summary>
	public const int MaxRewriteLength = 500;

	/// <summary>A new rule with a fresh id.</summary>
	public static HighlightRule Create(string pattern) => new(Guid.NewGuid().ToString("N")[..12], pattern);

	/// <summary>The rule does something to a line it matches.</summary>
	[JsonIgnore]
	public bool HasEffect => HasLook || Sound != HighlightSound.None || Notify || Hide || Stop || !string.IsNullOrEmpty(Rewrite);

	/// <summary>The rule changes how the text it targets looks.</summary>
	[JsonIgnore]
	public bool HasLook => Colour != HighlightColour.None || Style != HighlightStyle.None;
}

/// <summary>How a rule's pattern is read.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HighlightMatch>))]
public enum HighlightMatch
{
	/// <summary>The words as typed, as whole words: <c>Ann</c> matches "Ann waves" but not "Anne".</summary>
	Words,

	/// <summary>The text as typed, anywhere, inside a longer word too: <c>Ann</c> matches "Anne".</summary>
	Contains,

	/// <summary>The text as typed, at the start of the line.</summary>
	StartsWith,

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

/// <summary>What a rule's look and rewrite apply to.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HighlightTarget>))]
public enum HighlightTarget
{
	/// <summary>The whole line.</summary>
	Line,

	/// <summary>Every place in the line the pattern matches.</summary>
	Match,

	/// <summary>One group of the pattern (<see cref="HighlightRule.Group"/>), wherever the pattern matches.</summary>
	Group
}

/// <summary>How a rule's colour is put on the text.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HighlightPaint>))]
public enum HighlightPaint
{
	/// <summary>A tint behind the text (and, on a whole line, a bar at its start).</summary>
	Tint,

	/// <summary>The text itself takes the colour.</summary>
	Text
}

/// <summary>The text styles a rule can add.</summary>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<HighlightStyle>))]
public enum HighlightStyle
{
	None = 0,
	Bold = 1,
	Italic = 2,
	Underline = 4,
	Strike = 8,
	All = Bold | Italic | Underline | Strike
}

/// <summary>How a rule makes text look: its colour, put on as its paint says, and its styles.</summary>
public readonly record struct HighlightLook(HighlightColour Colour, HighlightPaint Paint, HighlightStyle Style)
{
	/// <summary>Leaves the text as it is.</summary>
	public static readonly HighlightLook None = new(HighlightColour.None, HighlightPaint.Tint, HighlightStyle.None);

	public bool IsNone => Colour == HighlightColour.None && Style == HighlightStyle.None;

	/// <summary>The CSS classes for this look (shell.css): <c>terminal-hl-*</c>.</summary>
	/// <param name="line">The look is for a whole line, whose tint also takes a bar at its start.</param>
	public string Classes(bool line)
	{
		if (IsNone) return string.Empty;
		var classes = new List<string>(6);
		if (Colour != HighlightColour.None)
		{
			classes.Add((line, Paint) switch
			{
				(true, HighlightPaint.Tint) => "terminal-hl",
				(false, HighlightPaint.Tint) => "terminal-mark",
				_ => "terminal-hl-text"
			});
			classes.Add($"terminal-hl-{Colour.ToString().ToLowerInvariant()}");
		}
		if (Style.HasFlag(HighlightStyle.Bold)) classes.Add("terminal-hl-bold");
		if (Style.HasFlag(HighlightStyle.Italic)) classes.Add("terminal-hl-italic");
		if (Style.HasFlag(HighlightStyle.Underline)) classes.Add("terminal-hl-underline");
		if (Style.HasFlag(HighlightStyle.Strike)) classes.Add("terminal-hl-strike");
		return string.Join(' ', classes);
	}
}
