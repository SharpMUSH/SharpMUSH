using OneOf;
using OneOf.Types;
using SharpMUSH.Client.Models;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side wiki service that delegates to <see cref="IWikiService"/> and projects
/// <see cref="WikiPage"/> results to the client <see cref="WikiArticle"/> view model.
/// </summary>
public class WikiService(IWikiService wikiService)
{
	public async ValueTask<OneOf<WikiArticle, None>> GetWikiArticle(string slug)
	{
		var result = await wikiService.GetBySlugAsync(slug);

		return result.Match<OneOf<WikiArticle, None>>(
			page => ToArticle(page),
			_ => new None()
		);
	}

	// ── Projection ────────────────────────────────────────────────────────────

	private static WikiArticle ToArticle(WikiPage page) =>
		new(
			title: page.Title,
			content: page.MarkdownSource,
			image: null,               // WikiPage has no image field yet
			renderedHtml: page.RenderedHtml
		);
}
