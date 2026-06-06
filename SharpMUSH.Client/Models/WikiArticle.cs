namespace SharpMUSH.Client.Models;

public class WikiArticle
{
	public WikiArticle(string title, string content, string? image, string? renderedHtml = null)
	{
		Title = title;
		Content = content;
		Image = image;
		RenderedHtml = renderedHtml;
	}

	public string Title { get; set; }

	/// <summary>Raw Markdown source (used as edit content and fallback render input).</summary>
	public string Content { get; set; }

	public string? Image { get; set; }

	/// <summary>
	/// Pre-rendered HTML produced by <see cref="SharpMUSH.Library.Services.WikiMarkdigPipeline"/>
	/// at write time. When non-null, <c>WikiDisplay</c> injects this directly and skips the
	/// client-side rendering pass. When null, the component falls back to rendering
	/// <see cref="Content"/> via the injected pipeline.
	/// </summary>
	public string? RenderedHtml { get; set; }
}
