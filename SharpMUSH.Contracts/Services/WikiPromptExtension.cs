using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Leaves the starter pages' notes to the game's administrators out of what readers see. A prompt is a block quote that
/// opens with <c>**Fill in:**</c> (<c>&gt; **Fill in:** How to reach staff.</c>); it is part of the page's Markdown, and
/// the editor shows it, but no rendering of the page does. A heading whose section held nothing but prompts goes with
/// them, so an unfinished page reads as a shorter page rather than a list of empty headings.
/// </summary>
public sealed class WikiPromptExtension : IMarkdownExtension
{
	/// <summary>The words a prompt opens with, in bold.</summary>
	public const string Marker = "Fill in:";

	public void Setup(MarkdownPipelineBuilder pipeline)
	{
		pipeline.DocumentProcessed -= Remove;
		pipeline.DocumentProcessed += Remove;
	}

	public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
	{
	}

	/// <summary>How many prompts <paramref name="markdown"/> still holds, by its source.</summary>
	public static int Count(string? markdown)
	{
		if (string.IsNullOrEmpty(markdown) || !markdown.Contains(Marker, StringComparison.Ordinal)) return 0;
		return Markdown.Parse(markdown).Descendants<QuoteBlock>().Count(IsPrompt);
	}

	/// <summary>Whether <paramref name="quote"/> is a prompt: its first paragraph opens with <c>**Fill in:**</c>.</summary>
	public static bool IsPrompt(QuoteBlock quote) =>
		quote.FirstOrDefault() is ParagraphBlock { Inline.FirstChild: EmphasisInline { DelimiterCount: 2 } strong }
		&& strong.FirstChild is LiteralInline literal
		&& literal.Content.ToString().Trim() == Marker;

	/// <summary>Takes every prompt out of <paramref name="document"/>, and each heading left with nothing under it.</summary>
	public static void Remove(MarkdownDocument document)
	{
		var prompts = document.Descendants<QuoteBlock>().Where(IsPrompt).ToList();
		if (prompts.Count == 0) return;

		var emptied = EmptiedHeadings(document, prompts.ToHashSet());
		foreach (var prompt in prompts)
		{
			// A quote that held nothing but the prompt goes with it, rather than rendering an empty quote.
			Block block = prompt;
			while (block.Parent is { } parent)
			{
				parent.Remove(block);
				if (parent is not QuoteBlock { Count: 0 }) break;
				block = parent;
			}
		}

		foreach (var heading in emptied)
		{
			document.Remove(heading);
		}
	}

	/// <summary>
	/// The page's headings whose section, every block up to the next heading of the same or a higher level, holds at
	/// least one prompt and nothing else but subheadings emptied the same way. Decided from the last heading up, so a
	/// subheading is settled before the heading over it; a subheading left empty on purpose keeps its parent.
	/// </summary>
	private static HashSet<HeadingBlock> EmptiedHeadings(MarkdownDocument document, HashSet<QuoteBlock> prompts)
	{
		var emptied = new HashSet<HeadingBlock>();
		for (var i = document.Count - 1; i >= 0; i--)
		{
			if (document[i] is not HeadingBlock heading) continue;

			var sawPrompt = false;
			var onlyPrompts = true;
			for (var j = i + 1; j < document.Count; j++)
			{
				var block = document[j];
				if (block is HeadingBlock next && next.Level <= heading.Level) break;
				if (block is QuoteBlock quote && prompts.Contains(quote) || block is HeadingBlock sub && emptied.Contains(sub))
				{
					sawPrompt = true;
					continue;
				}

				onlyPrompts = false;
				break;
			}

			if (sawPrompt && onlyPrompts)
			{
				emptied.Add(heading);
			}
		}

		return emptied;
	}
}
