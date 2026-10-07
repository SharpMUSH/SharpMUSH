using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkupString;
using MarkupString.Layout;
using Block = MarkupString.Layout.Block;
using System.Collections.Immutable;

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
		if (LoneImage(para) is { } image)
		{
			return RenderFigure(image, RenderInlines(image.FirstChild));
		}

		// Trim trailing whitespace because EnableTrackTrivia appends a soft
		// LineBreakInline (rendered as " ") at the end of many paragraphs.
		var content = RenderInlines(para.Inline);
		return content.Trim(TrimType.TrimEnd, " ");
	}

	/// <summary>The image a paragraph holds and nothing else but space, or null.</summary>
	private static LinkInline? LoneImage(ParagraphBlock para)
	{
		LinkInline? image = null;
		for (var inline = para.Inline?.FirstChild; inline is not null; inline = inline.NextSibling)
		{
			switch (inline)
			{
				case LinkInline { IsImage: true } link when image is null:
					image = link;
					break;
				case LineBreakInline:
				case LiteralInline literal when literal.Content.IsEmptyOrWhitespace():
					break;
				default:
					return null;
			}
		}
		return image;
	}

	/// <summary>
	/// A See Also footer (<see cref="SeeAlsoBlock"/>) as PennMUSH prints it: the label, then the topics
	/// separated by commas, wrapped at a comma with the continuation lines under the first topic.
	/// </summary>
	protected virtual MString RenderSeeAlso(SeeAlsoBlock seeAlso)
	{
		var label = $"{HelpSeeAlsoExtension.Label}: ";
		if (seeAlso.Items is null)
		{
			// An item says more than its name, so the contents print as written, under the label.
			return MarkupText.Concat([MarkupText.Wrap(_boldStyle, label.TrimEnd()), MarkupText.Plain("\n"),
				RenderContainerBlock(seeAlso)]);
		}

		var indent = MarkupText.Plain("\n" + new string(' ', label.Length));
		var parts = new List<MString> { MarkupText.Wrap(_boldStyle, label.TrimEnd()), MarkupText.Plain(" ") };
		var column = label.Length;
		var items = RenderSeeAlsoItems(seeAlso);
		for (var i = 0; i < items.Count; i++)
		{
			var width = items[i].ToPlainText().Length;
			if (i > 0)
			{
				parts.Add(MarkupText.Plain(","));
				column++;
				var trailingComma = i < items.Count - 1 ? 1 : 0;
				if (column + 1 + width + trailingComma > _maxWidth)
				{
					parts.Add(indent);
					column = label.Length;
				}
				else
				{
					parts.Add(MarkupText.Plain(" "));
					column++;
				}
			}
			parts.Add(items[i]);
			column += width;
		}
		return MarkupText.Concat(parts);
	}

	/// <summary>
	/// Each topic of a See Also footer of bare names, rendered (none if an item says more). A topic link is named bare (<c>@lock</c>, not
	/// <c>help @lock</c>): the label already says these are help topics, as in PennMUSH.
	/// </summary>
	protected IReadOnlyList<MString> RenderSeeAlsoItems(SeeAlsoBlock seeAlso)
	{
		_bareCommandLabels = true;
		try
		{
			return (seeAlso.Items ?? []).Select(Render).ToList();
		}
		finally
		{
			_bareCommandLabels = false;
		}
	}

	/// <summary>
	/// A list as <see cref="Bullets"/>: each item after its marker, an ordered list numbering from its own
	/// start, and a wrapped line hanging under the item's text rather than its marker.
	/// </summary>
	protected virtual MString RenderList(ListBlock list)
	{
		var items = list
			.OfType<ListItemBlock>()
			.Select(listItem => ItemBody(MarkupText.Join(MarkupText.NewLine, listItem
				.Select(Render)
				.Where(IsNonWhitespace)
				// Text keeps no spaces round it; a nested layout is left whole, so it is still a layout.
				.Select(rendered => BlockLayout.AsBlock(rendered) is TextBlock ? rendered.Trim(TrimType.TrimBoth, " ") : rendered))))
			.ToImmutableArray();
		if (items.IsEmpty) return MarkupText.Empty;

		// Star, not a styled custom marker: the portal draws a custom marker as an inline box ahead of the
		// item's text block, which puts the text on the line below it.
		return Laid(new Bullets(items) { Style = list.IsOrdered ? BulletStyle.Number : BulletStyle.Star, Start = FirstItemIndex(list) + 1 });
	}

	/// <summary>
	/// Rendered markdown as one block: each layout it holds (a nested list, a table) as its tree, so it is
	/// drawn again at the width it is given, and the text between them as text.
	/// </summary>
	protected static Block ItemBody(MString rendered)
	{
		var blocks = BlockLayout.Blocks(rendered);
		return blocks.Count switch
		{
			0 => new TextBlock(MarkupText.Empty),
			1 => blocks[0],
			_ => new Stack([.. blocks])
		};
	}

	/// <summary>
	/// The zero-based index the first item counts from, so the 1-based number every consumer derives
	/// as <c>index + 1</c> is the one the source asked for. Falls back to 1.
	/// </summary>
	protected static int FirstItemIndex(ListBlock list)
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
		=> Laid(new Rule().Colored(_dimStyle));

	/// <summary>Wiki directive names that render live listings on the web portal.</summary>
	private static readonly HashSet<string> WikiDirectiveNames =
		new(StringComparer.OrdinalIgnoreCase) { "category", "tag", "pagelist", "recent" };

	/// <summary>The container that centres its contents: <c>::: center</c>.</summary>
	public const string CenterContainerName = "center";

	/// <summary>
	/// Renders a <c>::: name args</c> custom container. The wiki's directive blocks
	/// (category/tag/pagelist/recent) are live web-portal listings that a terminal
	/// cannot resolve, so they render as a dimmed placeholder describing the listing.
	/// <c>::: center</c> centres each line of its contents in the render width, as the
	/// portal centres it in the page. Any other custom container renders its children
	/// like a normal block.
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
		var content = MarkupText.Join(MarkupText.Plain("\n"), parts);
		return name.Equals(CenterContainerName, StringComparison.OrdinalIgnoreCase) ? Centered(content) : content;
	}

	/// <summary>
	/// <paramref name="content"/> wrapped to the render width with each line centred in it: its text
	/// centred, and each layout it holds drawn with its text centred.
	/// </summary>
	private MString Centered(MString content)
		=> content.Length == 0 ? content : Laid(ItemBody(content).Aligned(Alignment.Center));

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
