using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

/// <summary>
/// The "See Also" footer most help entries end on, recognised once at parse time. It holds the source
/// list, so every topic link stays in the tree for whatever walks it (corpus prefixes, portal hrefs,
/// the link validator); <see cref="Items"/> are each item's one inline.
/// </summary>
public sealed class SeeAlsoBlock : ContainerBlock
{
	public SeeAlsoBlock(ListBlock list, IReadOnlyList<Inline> items) : base(null)
	{
		Add(list);
		Items = items;
	}

	/// <summary>Each topic in order: a topic link, or a code span naming something with no entry.</summary>
	public IReadOnlyList<Inline> Items { get; }
}

/// <summary>
/// Turns a See Also footer into a <see cref="SeeAlsoBlock"/>: a paragraph that is only <c>**See Also:**</c>,
/// followed by an unordered list whose every item is one topic reference (<c>- [@lock]</c>) or one code
/// span. Anything else — a description after a link, a nested list, a label that is not bold — is left as
/// the paragraph and list it was.
/// </summary>
/// <remarks>
/// Part of <see cref="RecursiveMarkdownHelper.ConfigureHelpSyntax"/>, so help, <c>rendermarkdown()</c> and
/// the portal's help pages all recognise the same footer; <c>rendermarkdowncustom()</c> can restyle it
/// with <c>RENDERMARKUP`SEEALSO</c>.
/// </remarks>
public sealed class HelpSeeAlsoExtension : IMarkdownExtension
{
	/// <summary>The label as it reads once the emphasis and colon are dropped.</summary>
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

	private static void Recognise(MarkdownDocument document)
	{
		foreach (var label in document.Descendants<ParagraphBlock>().ToList())
		{
			if (label.Parent is not { } parent || !IsLabel(label))
			{
				continue;
			}
			var index = parent.IndexOf(label);
			if (index + 1 >= parent.Count || parent[index + 1] is not ListBlock { IsOrdered: false } list
				|| Items(list) is not { } items)
			{
				continue;
			}

			parent.RemoveAt(index + 1);
			parent.RemoveAt(index);
			parent.Insert(index, new SeeAlsoBlock(list, items)
			{
				Line = label.Line,
				Span = new SourceSpan(label.Span.Start, list.Span.End),
				LinesBefore = label.LinesBefore,
				LinesAfter = list.LinesAfter
			});
		}
	}

	private static bool IsLabel(ParagraphBlock paragraph)
	{
		if (Significant(paragraph).ToList() is not [EmphasisInline { DelimiterCount: 2 } strong])
		{
			return false;
		}
		var text = string.Concat(strong.OfType<LiteralInline>().Select(literal => literal.Content.ToString()));
		return text.Trim().TrimEnd(':').Trim().Equals(Label, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Each item's one inline, or <see langword="null"/> if any item holds more than that.</summary>
	private static List<Inline>? Items(ListBlock list)
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

/// <summary>Writes a <see cref="SeeAlsoBlock"/> as a labelled navigation list of its topics.</summary>
public sealed class HtmlSeeAlsoRenderer : HtmlObjectRenderer<SeeAlsoBlock>
{
	protected override void Write(HtmlRenderer renderer, SeeAlsoBlock block)
	{
		renderer.EnsureLine();
		renderer.Write("<nav class=\"help-see-also\" aria-label=\"See also\">");
		renderer.Write($"<span class=\"help-see-also-label\">{HelpSeeAlsoExtension.Label}</span><ul>");
		foreach (var item in block.Items)
		{
			renderer.Write("<li>");
			renderer.Write(item);
			renderer.Write("</li>");
		}
		renderer.WriteLine("</ul></nav>");
	}
}

/// <summary><see cref="MarkdownPipelineBuilder"/> extension method for <see cref="HelpSeeAlsoExtension"/>.</summary>
public static class HelpSeeAlsoExtensions
{
	/// <summary>Recognises the helpfiles' See Also footer as a <see cref="SeeAlsoBlock"/>.</summary>
	public static MarkdownPipelineBuilder UseHelpSeeAlso(this MarkdownPipelineBuilder pipeline)
	{
		pipeline.Extensions.AddIfNotAlready<HelpSeeAlsoExtension>();
		return pipeline;
	}
}
