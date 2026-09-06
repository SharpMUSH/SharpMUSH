namespace MarkupString.Ansi;

/// <summary>
/// A markup layer carrying terminal formatting. Value-equal through <see cref="Style"/>, so two
/// identically styled spans coalesce.
/// </summary>
public sealed record AnsiMarkup(AnsiStyle Style) : IMarkup
{
	/// <summary>
	/// Builds a markup from individual attributes. A <see langword="null"/> colour means the span
	/// does not set that colour.
	/// </summary>
	public static AnsiMarkup Create(
		AnsiColor? foreground = null,
		AnsiColor? background = null,
		string? linkText = null,
		string? linkUrl = null,
		LinkKind linkKind = LinkKind.Url,
		bool blink = false,
		bool bold = false,
		bool clear = false,
		bool faint = false,
		bool inverted = false,
		bool italic = false,
		bool overlined = false,
		bool underlined = false,
		bool strikeThrough = false) =>
		new(new AnsiStyle
		{
			Foreground = foreground,
			Background = background,
			LinkText = linkText,
			LinkUrl = linkUrl,
			LinkKind = linkKind,
			Blink = blink,
			Bold = bold,
			Clear = clear,
			Faint = faint,
			Inverted = inverted,
			Italic = italic,
			Overlined = overlined,
			Underlined = underlined,
			StrikeThrough = strikeThrough
		});
}
