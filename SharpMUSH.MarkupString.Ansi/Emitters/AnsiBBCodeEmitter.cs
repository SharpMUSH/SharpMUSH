using System.Buffers;
namespace MarkupString.Ansi;

/// <summary>
/// Renders a run as BBCode. The vocabulary is small: one colour, four attributes and a URL tag.
/// Faint, blink and overline have no BBCode spelling and are dropped, and so is a command link —
/// a forum post has nowhere to send it.
/// </summary>
public sealed class AnsiBBCodeEmitter : IMarkupSetEmitter
{
	/// <inheritdoc/>
	public MarkupFormat Format => MarkupFormat.BBCode;

	/// <inheritdoc/>
	public bool TryEmit(MarkupSet set, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(set);
		ArgumentNullException.ThrowIfNull(output);

		var style = AnsiEmitterSupport.Fold(set, context.Format);

		// Reverse video has no BBCode form either, so the colour that would show as the text colour
		// is the one written.
		var foreground = style.Inverted ? style.Background : style.Foreground;
		var hex = foreground?.ToHex() ?? string.Empty;

		var link = style.LinkKind == LinkKind.Url
			&& style.LinkUrl is { Length: > 0 } candidate
			&& UrlSafety.IsSafeNavigableUrl(candidate)
			? candidate
			: null;

		using var core = new PooledCharWriter(body.Length + 64);

		if (hex.Length > 0)
		{
			core.Write("[color=");
			core.Write(hex);
			core.Write("]");
		}
		if (link is not null)
		{
			core.Write("[url=");
			core.Write(link);
			core.Write("]");
		}
		if (style.Bold) core.Write("[b]");
		if (style.Italic) core.Write("[i]");
		if (style.Underlined) core.Write("[u]");
		if (style.StrikeThrough) core.Write("[s]");

		core.Write(body);

		if (style.StrikeThrough) core.Write("[/s]");
		if (style.Underlined) core.Write("[/u]");
		if (style.Italic) core.Write("[/i]");
		if (style.Bold) core.Write("[/b]");
		if (link is not null) core.Write("[/url]");
		if (hex.Length > 0) core.Write("[/color]");

		AnsiEmitterSupport.WriteWrapped(set, core.WrittenSpan, context, output);
		return true;
	}
}
