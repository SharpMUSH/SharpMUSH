using MarkupString.Ansi;
namespace MarkupString.Html;

/// <summary>
/// A markup layer carrying one raw HTML tag: <c>&lt;{TagName} {Attributes}&gt;…&lt;/{TagName}&gt;</c>.
/// Value-equal through <see cref="TagName"/> and <see cref="Attributes"/>, so two identically
/// tagged spans coalesce.
/// </summary>
/// <remarks>
/// Neither <see cref="TagName"/> nor <see cref="Attributes"/> is sanitised or encoded — callers are
/// expected to have already validated them, the same way the format this replaces did.
/// </remarks>
public sealed record HtmlMarkup(string TagName, string? Attributes) : IMarkup, IAnsiStyleSource
{
	/// <summary>Creates a layer for <paramref name="tagName"/>, optionally with a raw attribute string.</summary>
	public static HtmlMarkup Create(string tagName, string? attributes = null) => new(tagName, attributes);

	/// <inheritdoc/>
	/// <remarks>
	/// Only <see cref="MarkupFormat.Ansi"/> and <see cref="MarkupFormat.BBCode"/> have a terminal
	/// equivalent for a handful of tags — <c>b</c>/<c>strong</c>, <c>i</c>/<c>em</c>, <c>u</c>, and
	/// <c>s</c>/<c>strike</c>/<c>del</c> — so those tags fold into the run's style there. Every other
	/// tag, and every tag in Html/Pueblo/Mxp, answers <see langword="false"/>: Html/Pueblo/Mxp
	/// render the tag itself through <see cref="HtmlTagEmitter"/>, and an unknown tag in
	/// Ansi/BBCode leaves its body untouched.
	/// </remarks>
	public bool TryGetAnsiStyle(MarkupFormat format, out AnsiStyle style)
	{
		if (format != MarkupFormat.Ansi && format != MarkupFormat.BBCode)
		{
			style = AnsiStyle.None;
			return false;
		}

		switch (TagName.ToLowerInvariant())
		{
			case "b" or "strong":
				style = AnsiStyle.None with { Bold = true };
				return true;
			case "i" or "em":
				style = AnsiStyle.None with { Italic = true };
				return true;
			case "u":
				style = AnsiStyle.None with { Underlined = true };
				return true;
			case "s" or "strike" or "del":
				style = AnsiStyle.None with { StrikeThrough = true };
				return true;
			default:
				style = AnsiStyle.None;
				return false;
		}
	}
}
