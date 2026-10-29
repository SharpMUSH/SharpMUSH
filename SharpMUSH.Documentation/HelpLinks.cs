using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;

namespace SharpMUSH.Documentation;

/// <summary>The help topics a piece of help markdown links to, as <c>[topic]</c> or under <c>::: seealso</c>.</summary>
public static class HelpLinks
{
	private static readonly MarkdownPipeline Pipeline =
		RecursiveMarkdownHelper.ConfigureHelpSyntax(new MarkdownPipelineBuilder()).Build();

	/// <summary>Each linked topic once, spelled as the first link spells it, in reading order.</summary>
	public static IReadOnlyList<string> Targets(string markdown) =>
		Markdown.Parse(markdown, Pipeline).Descendants<LinkInline>()
			.Where(link => link.GetData(HelpTopicInlineParser.CommandDataKey) is true)
			.Select(link => link.Url![5..])
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
}
