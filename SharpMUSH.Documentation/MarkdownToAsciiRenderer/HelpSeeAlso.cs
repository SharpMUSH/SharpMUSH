using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

/// <summary>
/// A <c>::: seealso</c> block: the topics an entry points the reader on to. It keeps the container's
/// contents as its children, so every topic link stays in the tree for whatever walks it (corpus
/// prefixes, portal hrefs, the link validator).
/// </summary>
public sealed class SeeAlsoBlock : ContainerBlock
{
	public SeeAlsoBlock() : base(null)
	{
	}

	/// <summary>
	/// Each topic in order — a topic link, or a code span naming something with no entry — when the
	/// block is one list of bare names; <see langword="null"/> when an item says more than its name, in
	/// which case the list renders as a list under the label.
	/// </summary>
	public IReadOnlyList<Inline>? Items { get; init; }
}

/// <summary>
/// Turns a <c>::: seealso</c> custom container into a <see cref="SeeAlsoBlock"/>, the way the wiki's
/// <c>::: category</c> and friends are custom containers with a meaning of their own:
/// <code>
/// ::: seealso
/// - [@lock]
/// - [@unlock]
/// - `[NO_TEL]`
/// :::
/// </code>
/// The terminal prints that as PennMUSH does, <c>See Also: @lock, @unlock, [NO_TEL]</c>; the portal
/// as a labelled row of links.
/// </summary>
/// <remarks>
/// Part of <see cref="RecursiveMarkdownHelper.ConfigureHelpSyntax"/>, so help, <c>rendermarkdown()</c> and
/// the portal's help pages read it alike; <c>rendermarkdowncustom()</c> hands it to
/// <c>RENDERMARKUP`CONTAINER</c> under the name <c>seealso</c>.
/// </remarks>
public sealed class HelpSeeAlsoExtension : IMarkdownExtension
{
	/// <summary>The container name that marks a See Also block.</summary>
	public const string Name = "seealso";

	/// <summary>The label both renderers put in front of the topics.</summary>
	public const string Label = "See Also";

	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder pipeline)
	{
		pipeline.DocumentProcessed -= Recognise;
		pipeline.DocumentProcessed += Recognise;
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
	{
		if (renderer is HtmlRenderer html && !html.ObjectRenderers.Contains<HtmlSeeAlsoRenderer>())
		{
			html.ObjectRenderers.Insert(0, new HtmlSeeAlsoRenderer());
		}
	}

	/// <summary>
	/// The container's name: the first word of its fence line, which Markdig puts in <c>Info</c> alone or
	/// splits across <c>Info</c> and <c>Arguments</c> depending on trivia tracking.
	/// </summary>
	public static string ContainerName(CustomContainer container) =>
		$"{container.Info} {container.Arguments}".Trim().Split(' ', 2)[0];

	private static void Recognise(MarkdownDocument document)
	{
		foreach (var container in document.Descendants<CustomContainer>().ToList())
		{
			if (container.Parent is not { } parent
				|| !ContainerName(container).Equals(Name, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var children = container.ToList();
			container.Clear();
			var seeAlso = new SeeAlsoBlock
			{
				Items = children is [ListBlock { IsOrdered: false } list] ? Names(list) : null,
				Line = container.Line,
				Span = container.Span,
				LinesBefore = container.LinesBefore,
				LinesAfter = container.LinesAfter
			};
			foreach (var child in children)
			{
				seeAlso.Add(child);
			}

			var index = parent.IndexOf(container);
			parent.RemoveAt(index);
			parent.Insert(index, seeAlso);
		}
	}

	/// <summary>Each item's one name, or <see langword="null"/> if any item holds more than that.</summary>
	private static List<Inline>? Names(ListBlock list)
	{
		var items = new List<Inline>();
		foreach (var block in list)
		{
			if (block is not ListItemBlock { Count: 1 } item || item[0] is not ParagraphBlock paragraph)
			{
				return null;
			}
			switch (Significant(paragraph).ToList())
			{
				case [LinkInline link] when link.GetData(HelpTopicInlineParser.CommandDataKey) is true:
					items.Add(link);
					break;
				case [CodeInline code]:
					items.Add(code);
					break;
				default:
					return null;
			}
		}
		return items.Count > 0 ? items : null;
	}

	/// <summary>
	/// The paragraph's inlines, less line breaks and whitespace-only literals; a pipeline that tracks
	/// trivia leaves those trailing every paragraph.
	/// </summary>
	private static IEnumerable<Inline> Significant(ParagraphBlock paragraph)
	{
		for (var inline = paragraph.Inline?.FirstChild; inline is not null; inline = inline.NextSibling)
		{
			if (inline is LineBreakInline || inline is LiteralInline literal && literal.Content.IsEmptyOrWhitespace())
			{
				continue;
			}
			yield return inline;
		}
	}
}

/// <summary>
/// Writes a <see cref="SeeAlsoBlock"/> as a labelled navigation block: its names as one list of links,
/// or, when an item says more than its name, its contents as written.
/// </summary>
public sealed class HtmlSeeAlsoRenderer : HtmlObjectRenderer<SeeAlsoBlock>
{
	protected override void Write(HtmlRenderer renderer, SeeAlsoBlock block)
	{
		renderer.EnsureLine();
		renderer.Write("<nav class=\"help-see-also\" aria-label=\"See also\">");
		renderer.Write($"<span class=\"help-see-also-label\">{HelpSeeAlsoExtension.Label}</span>");
		if (block.Items is { } items)
		{
			renderer.Write("<ul class=\"help-see-also-names\">");
			foreach (var item in items)
			{
				renderer.Write("<li>");
				renderer.Write(item);
				renderer.Write("</li>");
			}
			renderer.Write("</ul>");
		}
		else
		{
			renderer.WriteLine();
			renderer.WriteChildren(block);
		}
		renderer.WriteLine("</nav>");
	}
}

/// <summary><see cref="MarkdownPipelineBuilder"/> extension method for <see cref="HelpSeeAlsoExtension"/>.</summary>
public static class HelpSeeAlsoExtensions
{
	/// <summary>Reads <c>::: seealso</c> containers as <see cref="SeeAlsoBlock"/>s.</summary>
	public static MarkdownPipelineBuilder UseHelpSeeAlso(this MarkdownPipelineBuilder pipeline)
	{
		pipeline.Extensions.AddIfNotAlready<HelpSeeAlsoExtension>();
		return pipeline;
	}
}
