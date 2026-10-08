namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// The colour vision a theme is made for, and the colours that stay apart for it.
/// </summary>
/// <remarks>
/// <para>
/// No browser tells a page about its reader's colour vision: there is no media query or setting for it, and the
/// colour filters operating systems offer are applied after the page is drawn, out of its sight. So the player says
/// which applies (<see cref="SharpMUSH.Library.API.CharacterAppearance.Vision"/>). With no theme chosen, that picks the
/// vision's own theme for the browser's light or dark setting (<see cref="ThemeId"/>); with any theme, it swaps the
/// status colours (<c>danger</c>, <c>success</c>, <c>info</c>, <c>special</c>) and the code colours for these.
/// </para>
/// <para>
/// Each set was chosen by simulating the vision (Machado, Oliveira and Fernandes 2009, at full severity, so it holds
/// for the milder "-anomaly" forms too) and keeping the colours as far apart in OKLab as the 4.5:1 contrast floor
/// allows, on Phosphor's and Daylight's surfaces. <see cref="Mono"/> sees lightness only: its status colours differ
/// in lightness, and its code colours are the typical ones, since eleven colours cannot all differ in lightness and
/// still read.
/// </para>
/// </remarks>
public static class ThemeVision
{
	/// <summary>The theme token that names the vision a theme is made for.</summary>
	public const string Key = "vision";

	/// <summary>Trichromatic colour vision; the default.</summary>
	public const string Typical = "typical";

	/// <summary>Red-weak or red-blind: protanomaly and protanopia. Reds look dark and close to greens.</summary>
	public const string Protan = "protan";

	/// <summary>Green-weak or green-blind: deuteranomaly and deuteranopia, the most common. Reds and greens look alike.</summary>
	public const string Deutan = "deutan";

	/// <summary>Blue-weak or blue-blind: tritanomaly and tritanopia. Blues look like greens, and yellows like pinks.</summary>
	public const string Tritan = "tritan";

	/// <summary>No colour: achromatopsia and blue cone monochromacy. Only lightness tells colours apart.</summary>
	public const string Mono = "mono";

	public static readonly IReadOnlyList<string> All = [Typical, Protan, Deutan, Tritan, Mono];

	/// <summary><paramref name="vision"/> if it is one of <see cref="All"/> other than <see cref="Typical"/>, else null.</summary>
	public static string? Chosen(string? vision) => vision is not Typical && All.Contains(vision) ? vision : null;

	/// <summary>The vision a theme's tokens name, or <see cref="Typical"/>.</summary>
	public static string Of(IReadOnlyDictionary<string, string> tokens)
		=> tokens.TryGetValue(Key, out var vision) && All.Contains(vision) ? vision : Typical;

	/// <summary>The built-in theme for <paramref name="vision"/> in the browser's light or dark mode, or null for typical.</summary>
	public static string? ThemeId(string? vision, bool light) => Chosen(vision) is { } chosen ? $"{chosen}-{(light ? "light" : "dark")}" : null;

	/// <summary>
	/// The status colours a theme derives for <paramref name="vision"/> before they are fitted to its surfaces, in
	/// <see cref="ThemeResolver.StatusColors"/> order.
	/// </summary>
	public static IReadOnlyList<(string Name, string Hex)> StatusColors(string vision, bool dark)
		=> Palettes.TryGetValue((vision, dark), out var palette) ? palette.Status : ThemeResolver.StatusColors;

	/// <summary>The code colours a theme derives for <paramref name="vision"/>, in <see cref="ThemeResolver.SyntaxColors"/> order.</summary>
	public static IReadOnlyList<(string Name, string Hex)> SyntaxColors(string vision, bool dark)
		=> Palettes.TryGetValue((vision, dark), out var palette) && palette.Syntax is { } syntax ? syntax : ThemeResolver.SyntaxColors;

	private sealed record Palette(IReadOnlyList<(string Name, string Hex)> Status, IReadOnlyList<(string Name, string Hex)>? Syntax);

	private static readonly Dictionary<(string Vision, bool Dark), Palette> Palettes = new()
	{
		[(Protan, Dark: true)] = new(
			[("danger", "#f58b28"), ("success", "#2ca1c1"), ("info", "#9f5efe"), ("special", "#f41887")],
			[("syntax-command", "#d23dd5"), ("syntax-function", "#99c0fd"), ("syntax-substitution", "#e882a7"), ("syntax-dbref", "#92faed"), ("syntax-reference", "#cff20b"), ("syntax-at-command", "#ecbb11"), ("syntax-danger", "#e9470d"), ("syntax-string", "#d18f36"), ("syntax-link", "#3197fe"), ("syntax-heading", "#f72d7d"), ("syntax-emphasis", "#e4c4ad")]),
		[(Protan, Dark: false)] = new(
			[("danger", "#612f08"), ("success", "#0d155a"), ("info", "#5801f5"), ("special", "#9f0993")],
			[("syntax-command", "#035ce8"), ("syntax-function", "#086eae"), ("syntax-substitution", "#a22a46"), ("syntax-dbref", "#6510b3"), ("syntax-reference", "#2e7a0d"), ("syntax-at-command", "#6e0914"), ("syntax-danger", "#b8237e"), ("syntax-string", "#8c4d04"), ("syntax-link", "#1b766a"), ("syntax-heading", "#4c0980"), ("syntax-emphasis", "#66084d")]),
		[(Deutan, Dark: true)] = new(
			[("danger", "#f58b28"), ("success", "#21b4e0"), ("info", "#8869fe"), ("special", "#eb357b")],
			[("syntax-command", "#c1619a"), ("syntax-function", "#46aafc"), ("syntax-substitution", "#efaea1"), ("syntax-dbref", "#5e7ade"), ("syntax-reference", "#f5ff0d"), ("syntax-at-command", "#25f424"), ("syntax-danger", "#d95644"), ("syntax-string", "#e67b9e"), ("syntax-link", "#79bd2c"), ("syntax-heading", "#a8e1fc"), ("syntax-emphasis", "#dbf0b4")]),
		[(Deutan, Dark: false)] = new(
			[("danger", "#702504"), ("success", "#030964"), ("info", "#4f00f3"), ("special", "#c7108e")],
			[("syntax-command", "#a309c3"), ("syntax-function", "#0f748d"), ("syntax-substitution", "#675c1e"), ("syntax-dbref", "#5b02f0"), ("syntax-reference", "#280876"), ("syntax-at-command", "#690c10"), ("syntax-danger", "#d50613"), ("syntax-string", "#cf056d"), ("syntax-link", "#1c5e54"), ("syntax-heading", "#2f25a9"), ("syntax-emphasis", "#670b62")]),
		[(Tritan, Dark: true)] = new(
			[("danger", "#ff2018"), ("success", "#2dd2fd"), ("info", "#1380f5"), ("special", "#be43fc")],
			[("syntax-command", "#f9afe8"), ("syntax-function", "#a39ee1"), ("syntax-substitution", "#d143e9"), ("syntax-dbref", "#6479ed"), ("syntax-reference", "#feee6d"), ("syntax-at-command", "#a559f4"), ("syntax-danger", "#fd2222"), ("syntax-string", "#c9a147"), ("syntax-link", "#64e79a"), ("syntax-heading", "#bdf0f1"), ("syntax-emphasis", "#44c435")]),
		[(Tritan, Dark: false)] = new(
			[("danger", "#700612"), ("success", "#027398"), ("info", "#052c63"), ("special", "#760abb")],
			[("syntax-command", "#b321bc"), ("syntax-function", "#6821d4"), ("syntax-substitution", "#605008"), ("syntax-dbref", "#0f4757"), ("syntax-reference", "#1e1171"), ("syntax-at-command", "#631116"), ("syntax-danger", "#d41401"), ("syntax-string", "#846717"), ("syntax-link", "#24766d"), ("syntax-heading", "#8b1956"), ("syntax-emphasis", "#4f166c")]),
		[(Mono, Dark: true)] = new(
			[("danger", "#bf6767"), ("success", "#ade2cc"), ("info", "#ccd3da"), ("special", "#af5fd4")],
			null),
		[(Mono, Dark: false)] = new(
			[("danger", "#561b16"), ("success", "#297295"), ("info", "#1f264c"), ("special", "#984aa8")],
			null),
	};
}
