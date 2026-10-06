using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;

namespace SharpMUSH.Library.Services;

/// <summary>What is wrong with a <c>:::</c> fence.</summary>
public enum MarkdownFenceProblem
{
	/// <summary>A fence with no name opened a block: usually a closing fence that the block around it already used up.</summary>
	Unnamed,

	/// <summary>A block never found its closing fence and runs to the end of the page (or of the block around it).</summary>
	Unclosed,

	/// <summary>Content directly inside a <c>flex</c> but not in an <c>item</c>, which becomes a column of its own.</summary>
	OutsideItem,

	/// <summary>An <c>item</c> that is not directly inside a <c>flex</c>, so it lays nothing out.</summary>
	ItemOutsideFlex
}

/// <summary>One fence problem, at a 1-based source line.</summary>
public readonly record struct MarkdownFenceIssue(int Line, MarkdownFenceProblem Problem);

/// <summary>
/// Finds <c>:::</c> blocks that do not nest the way their author meant. A block closes on the first fence with at
/// least as many colons as its opener, so a block holding others needs more colons than they have; get that
/// wrong and the closing fence meant for an inner block closes the outer one, and the outer block's own closing
/// fence opens an empty block. Markdown has no errors, so nothing else would say so.
/// </summary>
public static class MarkdownFenceCheck
{
	/// <summary>
	/// The plain container parse, before any extension gives a container a meaning, so every fence is seen as written.
	/// </summary>
	private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

	/// <summary>Every fence problem in <paramref name="markdown"/>, in source order.</summary>
	public static IReadOnlyList<MarkdownFenceIssue> Check(string markdown)
	{
		if (string.IsNullOrEmpty(markdown) || !markdown.Contains(":::", StringComparison.Ordinal)) return [];

		var issues = new List<MarkdownFenceIssue>();
		foreach (var container in Markdown.Parse(markdown, Pipeline).Descendants<CustomContainer>())
		{
			var name = MarkdownFlexExtension.ContainerName(container);
			var line = container.Line + 1;

			if (name.Length == 0)
			{
				issues.Add(new MarkdownFenceIssue(line, MarkdownFenceProblem.Unnamed));
				continue;
			}

			if (container.ClosingFencedCharCount == 0)
			{
				issues.Add(new MarkdownFenceIssue(line, MarkdownFenceProblem.Unclosed));
			}

			if (name.Equals(MarkdownFlexExtension.FlexName, StringComparison.OrdinalIgnoreCase))
			{
				issues.AddRange(container
					.Where(child => !IsNamed(child, MarkdownFlexExtension.ItemName) && !IsUnnamed(child))
					.Select(child => new MarkdownFenceIssue(child.Line + 1, MarkdownFenceProblem.OutsideItem)));
			}
			else if (name.Equals(MarkdownFlexExtension.ItemName, StringComparison.OrdinalIgnoreCase)
				&& !(container.Parent is CustomContainer parent && IsNamed(parent, MarkdownFlexExtension.FlexName)))
			{
				issues.Add(new MarkdownFenceIssue(line, MarkdownFenceProblem.ItemOutsideFlex));
			}
		}

		return issues.OrderBy(issue => issue.Line).ToList();
	}

	private static bool IsNamed(Block block, string name) =>
		block is CustomContainer container
		&& MarkdownFlexExtension.ContainerName(container).Equals(name, StringComparison.OrdinalIgnoreCase);

	/// <summary>A stray unnamed block is reported on its own, not again as content outside an item.</summary>
	private static bool IsUnnamed(Block block) =>
		block is CustomContainer container && MarkdownFlexExtension.ContainerName(container).Length == 0;
}
