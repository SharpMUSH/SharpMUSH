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

	/// <summary>Server-side storage ID. Empty for locally-created (unsaved) pages.</summary>
	public string Id { get; set; } = string.Empty;

	/// <summary>URL slug used to address this page via the REST API. Empty for unsaved pages.</summary>
	public string Slug { get; set; } = string.Empty;

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

	/// <summary>Optional category grouping (lower-case), or null when uncategorised.</summary>
	public string? Category { get; set; }

	/// <summary>Searchable tags (lower-case, de-duplicated).</summary>
	public List<string> Tags { get; set; } = [];

	/// <summary>When false, the page is a draft hidden from anonymous visitors.</summary>
	public bool Published { get; set; } = true;

	/// <summary>The locale whose content this article actually carries.</summary>
	public string Locale { get; set; } = string.Empty;

	/// <summary>The locale the reader asked for, after server-side normalisation.</summary>
	public string RequestedLocale { get; set; } = string.Empty;

	/// <summary>True when a different language was served than requested — drives the fallback notice.</summary>
	public bool IsFallback { get; set; }

	/// <summary>Locales this reader can read the page in, source locale first.</summary>
	public List<string> AvailableLocales { get; set; } = [];

	/// <summary>
	/// Revision number of the row that was actually served — the translation's when a translation was
	/// served, the page's otherwise. <c>WikiEdit</c> passes it back as <c>expectedRevisionNumber</c> so a
	/// concurrent save is detected instead of silently overwritten.
	/// </summary>
	public int RevisionNumber { get; set; }
}
