namespace MarkupString.Html;

/// <summary>
/// The stylesheet for the <c>ms-*</c> classes the HTML emitters write for attributes that have a
/// fixed rendering — as opposed to colours, which are open-ended and go inline instead (see
/// <c>AnsiHtmlEmitter</c>). A page that renders this format's HTML output includes this once.
/// </summary>
public static class HtmlCss
{
	/// <summary>One rule per <c>ms-*</c> class this package's HTML emitters can write.</summary>
	public const string Fixed =
		".ms-bold { font-weight: bold; }\n" +
		".ms-faint { opacity: 0.5; }\n" +
		".ms-italic { font-style: italic; }\n" +
		".ms-underline { text-decoration: underline; }\n" +
		".ms-strike { text-decoration: line-through; }\n" +
		".ms-overline { text-decoration: overline; }\n" +
		".ms-blink { animation: blink 1s step-start infinite; }\n" +
		".ms-invert { color: var(--ms-bg, #000); background-color: var(--ms-fg, #fff); }\n";
}
