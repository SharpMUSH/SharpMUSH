namespace MarkupString.Ansi;

/// <summary>
/// The terminal formatting carried by one <see cref="AnsiMarkup"/> span: colours, attribute flags
/// and an optional link. A <see langword="null"/> colour means "not set by this span" — the colour
/// of whatever encloses it shows through.
/// </summary>
public readonly record struct AnsiStyle
{
	/// <summary>Foreground colour, or <see langword="null"/> when this span sets none.</summary>
	public AnsiColor? Foreground { get; init; }

	/// <summary>Background colour, or <see langword="null"/> when this span sets none.</summary>
	public AnsiColor? Background { get; init; }

	public bool Bold { get; init; }
	public bool Faint { get; init; }
	public bool Italic { get; init; }
	public bool Underlined { get; init; }
	public bool Overlined { get; init; }
	public bool Blink { get; init; }
	public bool Inverted { get; init; }
	public bool StrikeThrough { get; init; }

	/// <summary>
	/// Set by the <c>n</c> code: this span starts from a clean slate rather than inheriting the
	/// styling around it. See <see cref="Combine"/>.
	/// </summary>
	public bool Clear { get; init; }

	/// <summary>
	/// The link target: a URL when <see cref="LinkKind"/> is <see cref="LinkKind.Url"/>, otherwise
	/// the MUSH command to send.
	/// </summary>
	public string? LinkUrl { get; init; }

	/// <summary>The link's hover hint, if any.</summary>
	public string? LinkText { get; init; }

	/// <summary>Whether <see cref="LinkUrl"/> navigates or runs a command.</summary>
	public LinkKind LinkKind { get; init; }

	/// <summary>The style that changes nothing.</summary>
	public static readonly AnsiStyle None = default;

	/// <summary>True when any colour is set or any attribute flag (including <see cref="Clear"/>) is on.</summary>
	public bool HasAnyAttribute =>
		Foreground is not null || Background is not null ||
		Bold || Faint || Italic || Underlined || Overlined || Blink || Inverted || StrikeThrough || Clear;

	/// <summary>
	/// True when this style would render nothing: no colours, no attributes and no link.
	/// <see cref="LinkText"/> on its own is inert — it is only a hint for a <see cref="LinkUrl"/>.
	/// </summary>
	public bool IsNone => !HasAnyAttribute && string.IsNullOrEmpty(LinkUrl);

	/// <summary>
	/// Flattens a nested span onto its surroundings: this is the enclosing style,
	/// <paramref name="inner"/> the nested one. Colours the inner span sets win, colours it leaves
	/// unset show through from the outer, and attribute flags are the union of both. A link on the
	/// inner span replaces the outer one wholesale (target, hint and kind together).
	/// <para>
	/// When <paramref name="inner"/> has <see cref="Clear"/> set, the outer style is discarded
	/// first, so the result carries only what the inner span itself declares.
	/// </para>
	/// </summary>
	public AnsiStyle Combine(AnsiStyle inner)
	{
		var outer = inner.Clear ? None : this;

		var (linkUrl, linkText, linkKind) = string.IsNullOrEmpty(inner.LinkUrl)
			? (outer.LinkUrl, outer.LinkText, outer.LinkKind)
			: (inner.LinkUrl, inner.LinkText, inner.LinkKind);

		return new AnsiStyle
		{
			Foreground = inner.Foreground ?? outer.Foreground,
			Background = inner.Background ?? outer.Background,
			Bold = outer.Bold || inner.Bold,
			Faint = outer.Faint || inner.Faint,
			Italic = outer.Italic || inner.Italic,
			Underlined = outer.Underlined || inner.Underlined,
			Overlined = outer.Overlined || inner.Overlined,
			Blink = outer.Blink || inner.Blink,
			Inverted = outer.Inverted || inner.Inverted,
			StrikeThrough = outer.StrikeThrough || inner.StrikeThrough,
			Clear = inner.Clear,
			LinkUrl = linkUrl,
			LinkText = linkText,
			LinkKind = linkKind
		};
	}
}
