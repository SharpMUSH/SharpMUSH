using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Library.Logging;
using SharpMUSH.Server.Middleware;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Revision history and rollback. Revision numbering restarts at 1 per locale, so every route here
/// resolves which stream a request is in before it resolves a number — see
/// <see cref="WikiControllerBase.ResolveRevisionStreamAsync"/>.
///
/// Routes:
///   GET  /api/wiki/{slug}/revisions      — revision history (newest first)
///   GET  /api/wiki/{slug}/revisions/{n}  — single revision snapshot
///   POST /api/wiki/{slug}/rollback       — restore an earlier revision (authenticated)
/// </summary>
[ApiController]
[Route("api/wiki")]
public class WikiRevisionsController(
	IWikiService wikiService,
	IWikiLocalizationService localization,
	IWikiAccessService access,
	IPrerenderCacheService prerenderCache,
	IWikiNameResolver names,
	ILogger<WikiRevisionsController> logger) : WikiControllerBase(wikiService, localization, access, names, logger)
{
	/// <summary>
	/// GET /api/wiki/{slug}/revisions?skip=&amp;take=&amp;ns=&amp;lang=&amp;before=
	/// Revision history, newest first. Omitting <c>lang</c> (or naming the page's source locale) returns
	/// the source-locale stream. <c>before</c>, the last revision number of the previous page, pages by
	/// cursor instead of by <c>skip</c>, which it then ignores.
	/// </summary>
	[HttpGet("{slug}/revisions")]
	public async Task<IActionResult> GetRevisions(
		string slug, [FromQuery] int skip = 0, [FromQuery] int take = 20,
		[FromQuery] string? ns = null,
		[FromQuery] string? lang = null, [FromQuery] int? before = null)
	{
		// Mirror GetPage: drafts (and their history) are hidden from anonymous callers.
		if (await Wiki.GetBySlugAsync(slug, ParseNamespace(ns)) is not WikiPage page || !await CanSeeAsync(page))
			return NotFound();

		// The source page's revisions are stored with an empty Locale; a translation's carry its tag.
		var stream = await ResolveRevisionStreamAsync(page, lang);

		var revisions = before is { } cursor
			? await Wiki.GetRevisionsBeforeForLocaleAsync(page.Id, stream, cursor, take)
			: await Wiki.GetRevisionsForLocaleAsync(page.Id, stream, skip, take);
		return Ok(await ToDtosAsync(revisions));
	}

	/// <summary>
	/// GET /api/wiki/{slug}/revisions/{number}?ns=&amp;lang=
	/// Returns a single revision snapshot, including its full markdown body.
	/// </summary>
	/// <remarks>
	/// <c>lang</c> selects the same stream <see cref="GetRevisions"/> would list, and it is not optional
	/// decoration: revision numbering restarts at 1 per locale, so a number resolved against the wrong
	/// stream silently returns a different language's prose. The history page would then diff French
	/// against English and render it as a legitimate rewrite.
	/// </remarks>
	[HttpGet("{slug}/revisions/{number:int}")]
	public async Task<IActionResult> GetRevision(
		string slug, int number,
		[FromQuery] string? ns = null,
		[FromQuery] string? lang = null)
	{
		// Mirror GetPage: drafts (and their history) are hidden from anonymous callers.
		if (await Wiki.GetBySlugAsync(slug, ParseNamespace(ns)) is not WikiPage page || !await CanSeeAsync(page))
			return NotFound();

		var stream = await ResolveRevisionStreamAsync(page, lang);

		return await Wiki.GetRevisionForLocaleAsync(page.Id, stream, number) is WikiRevision revision
			? Ok(await ToDtoAsync(revision))
			: NotFound();
	}

	/// <summary>
	/// POST /api/wiki/{slug}/rollback
	/// Restores the page body from an earlier revision snapshot. The restore is a
	/// normal edit — it creates a NEW revision rather than rewriting history, so
	/// a rollback can itself be rolled back.
	/// </summary>
	[HttpPost("{slug}/rollback")]
	[Authorize]
	public async Task<IActionResult> RollbackPage(string slug, [FromBody] RollbackRequest request, [FromQuery] string? ns = null)
	{
		var editorDbref = CallerDbref;
		if (string.IsNullOrEmpty(editorDbref))
			return Unauthorized("Missing character identity.");
		if (await Wiki.GetBySlugAsync(slug, ParseNamespace(ns)) is not WikiPage page)
			return NotFound();

		if (await RefusalAsync(page, WikiAction.Edit) is { } refusal)
			return refusal;

		if (await Wiki.GetRevisionAsync(page.Id, request.RevisionNumber) is not WikiRevision revision)
			return NotFound();

		if (await Wiki.UpdateAsync(
				page.Id, revision.MarkdownSource, editorDbref, $"rollback to r{request.RevisionNumber}")
			is not WikiPage updated)
			return NotFound();

		Logger.LogInformation("Wiki page rolled back: slug={Slug} to r{Target} (now r{Rev}) by={Editor}",
			LogSanitizer.Sanitize(slug), request.RevisionNumber, updated.RevisionNumber, LogSanitizer.Sanitize(editorDbref));
		prerenderCache.InvalidatePrefix("/wiki/");
		return Ok(await ToDtoAsync(updated));
	}
}
