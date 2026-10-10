using System.Collections.Frozen;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// Text coloured by a theme colour's name (<c>tone()</c>) rather than by a colour, so each reader sees it in
/// their own theme: a telnet reader in their <c>@theme</c>, a portal reader in their portal theme.
/// </summary>
/// <remarks>
/// The text is marked with a <c>span</c> whose class names the tone, <c>tone tone-info</c>, which the portal
/// colours from its theme. Its <c>color</c> attribute holds what a reader without a theme is sent: the game's
/// <c>layout_theme</c> colour when it was written, or a standard colour. The rendering worker swaps the span for
/// the colour (<see cref="ForTelnet"/>), so a Pueblo or MXP client is never sent the tag.
/// </remarks>
public static class ToneMarkup
{
	private const string Prefix = "tone-";

	/// <summary>
	/// The tones by name: every theme colour that is painted on text under its own name, <c>highlight</c> painted
	/// behind the text, and <c>strong</c>, the foreground in bold.
	/// </summary>
	public static readonly FrozenDictionary<string, Tone> Tones = new Tone[]
	{
		new("foreground", ThemeRole.Foreground), new("strong", ThemeRole.Foreground, ThemePaint.Bold),
		new("primary", ThemeRole.Primary), new("secondary", ThemeRole.Secondary), new("tertiary", ThemeRole.Tertiary),
		new("muted", ThemeRole.Muted), new("subtle", ThemeRole.Subtle), new("link", ThemeRole.Link),
		new("highlight", ThemeRole.Highlight, ThemePaint.Background),
		new("success", ThemeRole.Success), new("warning", ThemeRole.Warning), new("error", ThemeRole.Error), new("info", ThemeRole.Info),
		new("red", ThemeRole.Red), new("orange", ThemeRole.Orange), new("yellow", ThemeRole.Yellow), new("green", ThemeRole.Green),
		new("cyan", ThemeRole.Cyan), new("blue", ThemeRole.Blue), new("purple", ThemeRole.Purple), new("pink", ThemeRole.Pink),
	}.ToFrozenDictionary(tone => tone.Name, StringComparer.OrdinalIgnoreCase);

	/// <summary>The tone <paramref name="name"/> names, ignoring case and surrounding spaces.</summary>
	public static bool TryParse(string name, out Tone tone) => Tones.TryGetValue(name.Trim(), out tone);

	/// <summary>The tone that paints <paramref name="role"/> on text under the role's own name.</summary>
	public static Tone Of(ThemeRole role) => Tones[ThemePalette.RoleName(role)];

	/// <summary>The standard colour a reader with no theme, in a game with none, is sent for <paramref name="role"/>.</summary>
	public static ThemeColor? Standard(ThemeRole role) => role switch
	{
		ThemeRole.Primary => ThemeColor.Standard(12),
		ThemeRole.Secondary => ThemeColor.Standard(13),
		ThemeRole.Tertiary => ThemeColor.Standard(3),
		ThemeRole.Muted or ThemeRole.Subtle or ThemeRole.Highlight => ThemeColor.Standard(8),
		ThemeRole.Success => ThemeColor.Standard(10),
		ThemeRole.Warning => ThemeColor.Standard(11),
		ThemeRole.Error => ThemeColor.Standard(9),
		ThemeRole.Info or ThemeRole.Cyan => ThemeColor.Standard(14),
		ThemeRole.Link => ThemeColor.Standard(6),
		ThemeRole.Red => ThemeColor.Standard(9),
		ThemeRole.Orange => ThemeColor.Standard(3),
		ThemeRole.Yellow => ThemeColor.Standard(11),
		ThemeRole.Green => ThemeColor.Standard(10),
		ThemeRole.Blue => ThemeColor.Standard(12),
		ThemeRole.Purple => ThemeColor.Standard(5),
		ThemeRole.Pink => ThemeColor.Standard(13),
		_ => null,
	};

	/// <summary><paramref name="text"/> in <paramref name="role"/>, sent as <paramref name="fallback"/> to a reader without a theme.</summary>
	public static MarkupText Build(ThemeRole role, MarkupText text, ThemeColor? fallback) => Build(Of(role), text, fallback);

	/// <summary><paramref name="text"/> in <paramref name="tone"/>, sent as <paramref name="fallback"/> to a reader without a theme.</summary>
	public static MarkupText Build(Tone tone, MarkupText text, ThemeColor? fallback)
	{
		var span = fallback is { } colour
			? HtmlMarkup.Tag("span", new HtmlAttribute("class", $"tone {Prefix}{tone.Name}"), new HtmlAttribute("color", Write(colour)))
			: HtmlMarkup.Tag("span", new HtmlAttribute("class", $"tone {Prefix}{tone.Name}"));
		return MarkupText.Wrap(span, text);
	}

	/// <summary>
	/// <paramref name="text"/> for a telnet client: each tone in <paramref name="palette"/>'s colour for it, or the
	/// colour written with it when the reader has no theme or the theme leaves that colour out; the span dropped
	/// either way. <c>strong</c> stays bold when there is no colour to send.
	/// </summary>
	public static MarkupText ForTelnet(MarkupText text, ThemePalette? palette)
	{
		if (text.Runs.IsDefaultOrEmpty || !text.Runs.Any(run => run.Markups.Any(IsTone))) return text;

		var parts = new List<MarkupText>();
		var position = 0;
		foreach (var run in text.Runs)
		{
			if (run.Start > position) parts.Add(MarkupText.Plain(text.Text[position..run.Start]));
			parts.Add(Rewrap(run.Markups, text.Text.Substring(run.Start, run.Length), palette));
			position = run.End;
		}
		if (position < text.Length) parts.Add(MarkupText.Plain(text.Text[position..]));
		return MarkupText.Concat(parts);
	}

	private static bool IsTone(IMarkup markup) => markup is HtmlMarkup { TagName: "span" } span && ToneOf(span) is not null;

	/// <summary>The tone a tone span names, or null for any other span.</summary>
	private static Tone? ToneOf(HtmlMarkup span) =>
		Attribute(span, "class") is { } classes
		&& classes.Split(' ', StringSplitOptions.RemoveEmptyEntries) is var names
		&& names.Contains("tone")
		&& names.FirstOrDefault(n => n.StartsWith(Prefix, StringComparison.Ordinal)) is { } named
		&& TryParse(named[Prefix.Length..], out var tone)
			? tone
			: null;

	private static string? Attribute(HtmlMarkup span, string name) =>
		HtmlMarkup.TryParseAttributes(span.Attributes, out var attributes)
			? attributes.Where(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)).Select(a => a.Value).FirstOrDefault()
			: null;

	/// <summary><paramref name="text"/> under a run's markups, each tone span replaced by its colour.</summary>
	private static MarkupText Rewrap(MarkupSet markups, string text, ThemePalette? palette)
	{
		var kept = new List<IMarkup>();
		foreach (var markup in markups)
		{
			if (markup is HtmlMarkup { TagName: "span" } span && ToneOf(span) is { } tone)
			{
				if ((palette?[tone.Role] ?? Read(Attribute(span, "color"))) is { } colour) kept.Add(AnsiTheme.Paint(colour, tone.Paint));
				else if (tone.Paint == ThemePaint.Bold) kept.Add(AnsiMarkup.Create(bold: true));
				continue;
			}
			kept.Add(markup);
		}
		return kept.Count == 0 ? MarkupText.Plain(text) : MarkupText.Wrap(MarkupSet.Of(kept), text);
	}

	/// <summary>A colour as the <c>color</c> attribute keeps it: <c>#rrggbb</c>, or a standard colour's number.</summary>
	private static string Write(ThemeColor colour) =>
		colour.Rgb is { } rgb ? rgb.ToHex() : (colour.Slot ?? 7).ToString(System.Globalization.CultureInfo.InvariantCulture);

	private static ThemeColor? Read(string? written) =>
		written is null ? null
		: ColorMath.TryParseHex(written, out var rgb) ? ThemeColor.Of(rgb)
		: int.TryParse(written, out var slot) && slot is >= 0 and <= 15 ? ThemeColor.Standard(slot)
		: null;
}

/// <summary>A name <c>tone()</c> takes: the theme colour it paints and how, on the text, in bold or behind it.</summary>
/// <param name="Name">The name, as <c>tone()</c> and the portal's <c>tone-*</c> class write it.</param>
/// <param name="Role">The theme colour.</param>
/// <param name="Paint">How it is painted.</param>
public readonly record struct Tone(string Name, ThemeRole Role, ThemePaint Paint = ThemePaint.Text);
