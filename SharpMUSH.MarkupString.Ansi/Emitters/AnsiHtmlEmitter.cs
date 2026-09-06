using System.Buffers;
using System.Net;
namespace MarkupString.Ansi;

/// <summary>
/// Renders a run as one HTML element: the folded style becomes a single
/// <c>&lt;span style="…" class="…"&gt;</c>, with colours as inline hex (they are open-ended, so a
/// stylesheet cannot enumerate them) and attributes as <c>ms-*</c> classes (a fixed set, which a
/// stylesheet can). A link becomes the anchor inside that span.
/// </summary>
public sealed class AnsiHtmlEmitter : IMarkupSetEmitter
{
	/// <inheritdoc/>
	public MarkupFormat Format => MarkupFormat.Html;

	/// <inheritdoc/>
	public bool TryEmit(MarkupSet set, ReadOnlySpan<char> body, in EmitContext context, IBufferWriter<char> output)
	{
		ArgumentNullException.ThrowIfNull(set);
		ArgumentNullException.ThrowIfNull(output);

		var style = AnsiEmitterSupport.Fold(set, context.Format);

		// Reverse video swaps the two colours rather than asking the browser to.
		var (foreground, background) = style.Inverted
			? (style.Background, style.Foreground)
			: (style.Foreground, style.Background);

		// The terminal default has no fixed value, so it contributes no declaration and the page's
		// own colour shows through.
		var foregroundHex = foreground?.ToHex() ?? string.Empty;
		var backgroundHex = background?.ToHex() ?? string.Empty;
		var hasStyle = foregroundHex.Length > 0 || backgroundHex.Length > 0;
		var hasClasses = HasClasses(style, hasStyle);

		using var core = new PooledCharWriter(body.Length + 96);

		if (hasStyle || hasClasses)
		{
			core.Write("<span");

			if (hasStyle)
			{
				core.Write(" style=\"");
				if (foregroundHex.Length > 0)
				{
					core.Write("color: ");
					core.Write(foregroundHex);
				}
				if (foregroundHex.Length > 0 && backgroundHex.Length > 0) core.Write("; ");
				if (backgroundHex.Length > 0)
				{
					core.Write("background-color: ");
					core.Write(backgroundHex);
				}
				core.Write("\"");
			}

			if (hasClasses)
			{
				core.Write(" class=\"");
				WriteClasses(style, hasStyle, core);
				core.Write("\"");
			}

			core.Write(">");
			WriteAnchored(style, body, core);
			core.Write("</span>");
		}
		else
		{
			WriteAnchored(style, body, core);
		}

		AnsiEmitterSupport.WriteWrapped(set, core.WrittenSpan, context, output);
		return true;
	}

	/// <summary>
	/// Whether the run needs a class attribute at all. <see cref="AnsiStyle.Inverted"/> only earns
	/// one when there is no colour for it to swap — <c>ms-invert</c> takes over the swap itself,
	/// via the fixed colours the stylesheet declares for it, once the run has none of its own.
	/// </summary>
	private static bool HasClasses(in AnsiStyle style, bool hasColourStyle) =>
		style.Bold || style.Faint || style.Italic || style.Underlined
		|| style.StrikeThrough || style.Overlined || style.Blink
		|| (style.Inverted && !hasColourStyle);

	private static void WriteClasses(in AnsiStyle style, bool hasColourStyle, IBufferWriter<char> output)
	{
		var first = true;
		AppendClass(style.Bold, "ms-bold", ref first, output);
		AppendClass(style.Faint, "ms-faint", ref first, output);
		AppendClass(style.Italic, "ms-italic", ref first, output);
		AppendClass(style.Underlined, "ms-underline", ref first, output);
		AppendClass(style.StrikeThrough, "ms-strike", ref first, output);
		AppendClass(style.Overlined, "ms-overline", ref first, output);
		AppendClass(style.Blink, "ms-blink", ref first, output);
		AppendClass(style.Inverted && !hasColourStyle, "ms-invert", ref first, output);
	}

	private static void AppendClass(bool on, string name, ref bool first, IBufferWriter<char> output)
	{
		if (!on) return;
		if (!first) output.Write(" ");
		first = false;
		output.Write(name);
	}

	/// <summary>
	/// Wraps the body in an anchor. A command link has no href — the terminal component intercepts
	/// <c>xch_cmd</c> — so it carries <c>role</c> and <c>tabindex</c> to stay keyboard-reachable. A
	/// URL link with a scheme <see cref="UrlSafety"/> rejects falls back to plain text.
	/// </summary>
	private static void WriteAnchored(in AnsiStyle style, ReadOnlySpan<char> body, IBufferWriter<char> output)
	{
		if (style.LinkUrl is not { Length: > 0 } url)
		{
			output.Write(body);
			return;
		}

		if (style.LinkKind == LinkKind.Command)
		{
			output.Write("<a class=\"ms-cmd-link\" role=\"button\" tabindex=\"0\" xch_cmd=\"");
			output.Write(WebUtility.HtmlEncode(url));
			output.Write("\"");
		}
		else if (UrlSafety.IsSafeNavigableUrl(url))
		{
			output.Write("<a href=\"");
			output.Write(WebUtility.HtmlEncode(url));
			output.Write("\" target=\"_blank\" rel=\"noopener noreferrer\"");
		}
		else
		{
			output.Write(body);
			return;
		}

		if (style.LinkText is { Length: > 0 } hint)
		{
			output.Write(" title=\"");
			output.Write(WebUtility.HtmlEncode(hint));
			output.Write("\"");
		}

		output.Write(">");
		output.Write(body);
		output.Write("</a>");
	}
}
