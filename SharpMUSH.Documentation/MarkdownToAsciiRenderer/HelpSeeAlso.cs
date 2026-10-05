using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

/// <summary>
/// Finds the "See Also" footer most help entries end on: a paragraph that is only <c>**See Also:**</c>,
/// followed by a list whose every item is one topic reference (<c>- [@lock]</c>) or one code span.
/// </summary>
/// <remarks>
/// Both renderers lay that footer out as one run of names rather than a list, the terminal as PennMUSH
/// prints it (<c>See Also: @lock, @unlock</c>) and the portal as a row of links. A list with anything
/// else in an item — a description after the link, a nested list — is not a footer and renders as a list.
/// </remarks>
public static class HelpSeeAlso
{
	/// <summary>The label as it reads once the emphasis and colon are dropped.</summary>
	public const string Label = "See Also";

	/// <summary>
	/// Whether <paramref name="label"/> is a See Also label with a footer list after it.
	/// </summary>
	/// <param name="label">The paragraph to test.</param>
	/// <param name="list">The list that follows the label.</param>
	/// <param name="items">Each item's one inline: a topic link or a code span.</param>
	public static bool TryMatch(ParagraphBlock label, out ListBlock list, out IReadOnlyList<Inline> items)
	{
		list = null!;
		items = [];
		if (!IsLabel(label) || label.Parent is not { } parent)
		{
			return false;
		}

		var index = parent.IndexOf(label);
		if (index < 0 || index + 1 >= parent.Count || parent[index + 1] is not ListBlock { IsOrdered: false } next)
		{
			return false;
		}

		var found = new List<Inline>();
		foreach (var block in next)
		{
			if (block is not ListItemBlock { Count: 1 } item || item[0] is not ParagraphBlock paragraph
				|| SingleInline(paragraph) is not { } inline)
			{
				return false;
			}
			found.Add(inline);
		}

		if (found.Count == 0)
		{
			return false;
		}
		list = next;
		items = found;
		return true;
	}

	private static bool IsLabel(ParagraphBlock paragraph)
	{
		var inlines = Significant(paragraph).ToList();
		if (inlines is not [EmphasisInline { DelimiterCount: 2 } strong])
		{
			return false;
		}
		var text = string.Concat(strong.OfType<LiteralInline>().Select(literal => literal.Content.ToString()));
		return text.Trim().TrimEnd(':').Trim().Equals(Label, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>The paragraph's one topic link or code span, or <see langword="null"/>.</summary>
	private static Inline? SingleInline(ParagraphBlock paragraph) =>
		Significant(paragraph).ToList() switch
		{
			[LinkInline link] when link.GetData(HelpTopicInlineParser.CommandDataKey) is true => link,
			[CodeInline code] => code,
			_ => null
		};

	/// <summary>The paragraph's inlines, less line breaks and whitespace that only trail the text.</summary>
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
