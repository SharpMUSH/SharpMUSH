using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

/// <summary>
/// Reads <c>\|</c> in a code span inside a table cell as <c>|</c>, as GitHub-flavoured markdown does,
/// so a cell such as <c>`key...\|value...`</c> shows <c>key...|value...</c>. Markdig keeps the
/// backslash; the Docs site, which reads the same files, needs it to keep the cell whole.
/// </summary>
/// <remarks>Part of <see cref="RecursiveMarkdownHelper.ConfigureHelpSyntax"/>, so every renderer reads it alike.</remarks>
public sealed class TableCodePipeExtension : IMarkdownExtension
{
	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder pipeline)
	{
		pipeline.DocumentProcessed -= Unescape;
		pipeline.DocumentProcessed += Unescape;
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline pipeline, Markdig.Renderers.IMarkdownRenderer renderer)
	{
	}

	private static void Unescape(MarkdownDocument document)
	{
		foreach (var code in document.Descendants<Table>().SelectMany(table => table.Descendants<CodeInline>())
			.Where(code => code.Content.Contains(@"\|", StringComparison.Ordinal)))
		{
			code.Content = code.Content.Replace(@"\|", "|", StringComparison.Ordinal);
		}
	}
}

/// <summary><see cref="MarkdownPipelineBuilder"/> extension method for <see cref="TableCodePipeExtension"/>.</summary>
public static class TableCodePipeExtensions
{
	/// <summary>Reads <c>\|</c> in a table cell's code span as <c>|</c>.</summary>
	public static MarkdownPipelineBuilder UseTableCodePipes(this MarkdownPipelineBuilder pipeline)
	{
		pipeline.Extensions.AddIfNotAlready<TableCodePipeExtension>();
		return pipeline;
	}
}
