using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;
using MarkupString;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public partial class RecursiveMarkdownRenderer
{
	protected virtual MString RenderHeading(HeadingBlock heading)
	{
		var style = heading.Level switch
		{
			1 or 2 => _headingStyle,
			3 => _heading3Style,
			_ => Ansi.Create()
		};

		var content = RenderInlines(heading.Inline);
		return MarkupText.Wrap(style, content.ToPlainText());
	}

	private MString RenderParagraph(ParagraphBlock para)
	{
		// Trim trailing whitespace because EnableTrackTrivia appends a soft
		// LineBreakInline (rendered as " ") at the end of many paragraphs.
		var content = RenderInlines(para.Inline);
		return content.Trim(TrimType.TrimEnd, " ");
	}

	/// <summary>One item per line, each with a marker; an ordered list numbers from its own start.</summary>
	private MString RenderList(ListBlock list)
	{
		var itemIndex = FirstItemIndex(list);
		var items = list
			.OfType<ListItemBlock>()
			.Select(listItem => RenderListItem(listItem, itemIndex++, list.IsOrdered))
			.ToList();

		return MarkupText.Join(MarkupText.Plain("\n"), items);
	}

	/// <summary>
	/// The zero-based index the first item counts from, so the 1-based number every consumer derives
	/// as <c>index + 1</c> is the one the source asked for. Falls back to 1.
	/// </summary>
	private static int FirstItemIndex(ListBlock list)
		=> list.IsOrdered && int.TryParse(list.OrderedStart, out var start) ? start - 1 : 0;

	protected MString ListMarker(int index, bool isOrdered)
		=> MarkupText.Wrap(_dimStyle, isOrdered ? $"{index + 1}. " : "* ");

	/// <summary>
	/// An item's rendered content, WITHOUT its marker, so a renderer that supplies its own marker —
	/// a <c>RENDERMARKUP`LISTITEM</c> template — does not end up prefixing two.
	/// </summary>
	protected MString RenderListItemContent(ListItemBlock listItem)
	{
		var parts = listItem
			.Select(child => Render(child))
			.Where(rendered => rendered.Length > 0)
			.ToList();

		return MarkupText.Concat(parts).Trim(TrimType.TrimBoth, " ");
	}

	protected virtual MString RenderListItem(ListItemBlock listItem, int index = 0, bool isOrdered = false)
		=> MarkupText.Concat(ListMarker(index, isOrdered), RenderListItemContent(listItem));

	protected virtual MString RenderQuote(QuoteBlock quote)
	{
		var parts = quote
			.Select(Render)
			.Where(rendered => rendered.Length > 0)
			.ToList();

		var content = MarkupText.Join(MarkupText.Plain("\n"), parts);

		// Indent every line by two columns. MarkupText.Split carries the markup across, so a
		// quote keeps its styling; going through ToPlainText here used to throw all of it away.
		if (content.Length == 0) return MarkupText.Empty;

		var indent = MarkupText.Plain("  ");
		var indented = content
			.Split("\n")
			.Select(line => MarkupText.Concat(indent, line))
			.ToArray();

		return MarkupText.Join(MarkupText.NewLine, indented);
	}

	private MString RenderThematicBreak()
		=> MarkupText.Wrap(_dimStyle, string.Concat(Enumerable.Repeat("-", _maxWidth)));

	/// <summary>Wiki directive names that render live listings on the web portal.</summary>
	private static readonly HashSet<string> WikiDirectiveNames =
		new(StringComparer.OrdinalIgnoreCase) { "category", "tag", "pagelist", "recent" };

	/// <summary>
	/// Renders a <c>::: name args</c> custom container. The wiki's directive blocks
	/// (category/tag/pagelist/recent) are live web-portal listings that a terminal
	/// cannot resolve, so they render as a dimmed placeholder describing the listing.
	/// Any other custom container renders its children like a normal block.
	/// </summary>
	protected virtual MString RenderCustomContainer(CustomContainer container)
	{
		// Depending on trivia tracking, Markdig may put the whole fence line in Info
		// or split it across Info/Arguments — normalise to "name" + "rest".
		var fenceLine = $"{container.Info} {container.Arguments}".Trim();
		var tokens = fenceLine.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		var name = tokens.Length > 0 ? tokens[0] : string.Empty;

		if (WikiDirectiveNames.Contains(name))
		{
			var arg = tokens.Length > 1 ? tokens[1] : string.Empty;
			var label = string.IsNullOrEmpty(arg) ? name : $"{name} {arg}";
			return MarkupText.Wrap(_dimStyle, $"[live listing: {label} — see the web portal]");
		}

		var parts = container
			.Select(child => Render(child))
			.Where(IsNonWhitespace)
			.ToList();
		return MarkupText.Join(MarkupText.Plain("\n"), parts);
	}

	private MString RenderHtmlBlock(HtmlBlock html)
	{
		// HtmlBlock is a LeafBlock — Markdig does not recurse into it or parse
		// child inlines. The raw HTML lines are all we have, so pass them through.
		var htmlContent = string.Join("\n", html.Lines.Lines
			.Take(html.Lines.Count)
			.Select(line => line.Slice.ToString()));
		return string.IsNullOrWhiteSpace(htmlContent)
			? MarkupText.Empty
			: MarkupText.Plain(htmlContent);
	}
}
