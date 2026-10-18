using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Renderers;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Keeps a wide wiki table in its own keyboard-scrollable container. The table renderer still
/// owns the table, headers, cell attributes and contents; only its surrounding layout changes.
/// </summary>
public sealed class WikiTableExtension : IMarkdownExtension
{
	public void Setup(MarkdownPipelineBuilder pipeline)
	{
	}

	public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
	{
		if (renderer is not HtmlRenderer htmlRenderer)
			return;

		var existing = htmlRenderer.ObjectRenderers.FindExact<HtmlTableRenderer>();
		if (existing is not null)
			htmlRenderer.ObjectRenderers.Remove(existing);

		htmlRenderer.ObjectRenderers.Insert(0, new ScrollableTableRenderer());
	}

	private sealed class ScrollableTableRenderer : HtmlTableRenderer
	{
		protected override void Write(HtmlRenderer renderer, Table table)
		{
			renderer.EnsureLine();
			renderer.WriteLine("<div class=\"wiki-table-scroll\" tabindex=\"0\">");
			base.Write(renderer, table);
			renderer.WriteLine("</div>");
		}
	}
}
