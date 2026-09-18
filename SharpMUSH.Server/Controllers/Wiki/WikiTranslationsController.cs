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
using SharpMUSH.Server.Helpers;
using SharpMUSH.Server.Middleware;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// One locale's view of a page. A translation is an edit to the page, so writes here are gated on
/// the page-edit scope and on the source page's <c>IsProtected</c>, exactly as an edit is.
///
/// Routes:
///   GET    /api/wiki/{slug}/translations           — locales this reader may read the page in
///   PUT    /api/wiki/{slug}/translations/{locale}  — create/update one locale (authenticated)
///   DELETE /api/wiki/{slug}/translations/{locale}  — remove one locale (authenticated)
/// </summary>
[ApiController]
[Route("api/wiki")]
public class WikiTranslationsController(
	IWikiService wikiService,
	IWikiLocalizationService localization,
	IPrerenderCacheService prerenderCache,
	ILogger<WikiTranslationsController> logger) : WikiControllerBase(wikiService, localization, logger)
{
	/// <summary>
	/// GET /api/wiki/{slug}/translations?ns=&amp;category=
	/// Lists the translations of a page that this reader may see. Drafts are omitted for readers without
	/// the edit scope, so the language chips and <c>hreflang</c> never advertise a page nobody can open.
	/// </summary>
	[HttpGet("{slug}/translations")]
	public async Task<IActionResult> GetTranslations(
		string slug, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		if (await Wiki.GetBySlugAsync(slug, category, ParseNamespace(ns)) is not WikiPage page || !CanSee(page))
			return NotFound();

		var summaries = await Localization.GetVisibleTranslationsAsync(page.Id, IncludeDrafts);
		return Ok(summaries.Select(ToDto));
	}

	/// <summary>
	/// PUT /api/wiki/{slug}/translations/{locale}?ns=&amp;category=
	/// Creates or updates one locale's translation. Gated on the page-edit scope and on the source page's
	/// <c>IsProtected</c>, exactly as <see cref="UpdatePage"/> is: a translation is an edit to the page.
	/// </summary>
	[HttpPut("{slug}/translations/{locale}")]
	[Authorize(Policy = PortalPermission.WikiEdit)]
	public async Task<IActionResult> PutTranslation(
		string slug, string locale, [FromBody] UpsertTranslationRequest request,
		[FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		var editorDbref = CallerDbref;
		if (string.IsNullOrEmpty(editorDbref))
			return Unauthorized("Missing character identity.");

		if (await Wiki.GetBySlugAsync(slug, category, ParseNamespace(ns)) is not WikiPage page)
			return NotFound();

		if (page.IsProtected && !User.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiAdmin))
			return Forbid();

		var result = await Wiki.UpsertTranslationAsync(
			page.Id, locale, request.Title, request.Markdown,
			editorDbref, request.EditSummary, request.Published, request.ExpectedRevisionNumber);

		return result switch
		{
			WikiTranslation translation => TranslationSaved(translation),
			// A lost write is 409, not 400: the request was well-formed and the client's correct response is
			// to reload, which is a different instruction from "your body was invalid". The endpoint does not
			// retry — retrying would re-apply this caller's stale markdown over the winner's, which is the
			// loss expectedRevisionNumber exists to prevent.
			WikiWriteConflict conflict => Conflict(new { error = ConflictMessage(conflict, locale), reload = true }),
			Error<string> err => BadRequest(new { error = err.Value })
		};

		IActionResult TranslationSaved(WikiTranslation translation)
		{
			Logger.LogInformation(
				"Wiki translation saved: slug={Slug} locale={Locale} rev={Rev} by={Editor}",
				LogSanitizer.Sanitize(slug), LogSanitizer.Sanitize(translation.Locale),
				translation.RevisionNumber, LogSanitizer.Sanitize(editorDbref));
			prerenderCache.InvalidatePrefix("/wiki/");
			return Ok(ToDto(new WikiTranslationSummary(
				translation.Locale, translation.Title, translation.Published,
				translation.UpdatedAt, translation.RevisionNumber)));
		}
	}

	/// <summary>
	/// Human wording for a <see cref="WikiWriteConflict"/>. Phrasing lives here rather than in the
	/// storage implementation: it is presentation.
	/// </summary>
	private static string ConflictMessage(WikiWriteConflict conflict, string locale) => conflict switch
	{
		WikiWriteConflict.AlreadyExists =>
			$"A '{locale}' translation already exists. Pass its current revision number to update it.",
		WikiWriteConflict.TranslationGone =>
			$"The '{locale}' translation was deleted while you were editing. Reload and re-apply your changes.",
		_ => $"The '{locale}' translation changed while you were editing. Reload and re-apply your changes."
	};

	/// <summary>
	/// DELETE /api/wiki/{slug}/translations/{locale}?ns=&amp;category=
	/// Removes one locale's translation and its revision stream. The page and every other translation are
	/// untouched, and deleting the last translation is allowed. Gated as an edit, not a page deletion.
	/// </summary>
	[HttpDelete("{slug}/translations/{locale}")]
	[Authorize(Policy = PortalPermission.WikiEdit)]
	public async Task<IActionResult> DeleteTranslation(
		string slug, string locale, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		var editorDbref = CallerDbref;
		if (string.IsNullOrEmpty(editorDbref))
			return Unauthorized("Missing character identity.");

		if (await Wiki.GetBySlugAsync(slug, category, ParseNamespace(ns)) is not WikiPage page)
			return NotFound();

		if (page.IsProtected && !User.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiAdmin))
			return Forbid();

		if (await Wiki.DeleteTranslationAsync(page.Id, locale, editorDbref) is not None)
			return NotFound();

		Logger.LogInformation(
			"Wiki translation deleted: slug={Slug} locale={Locale} by={Editor}",
			LogSanitizer.Sanitize(slug), LogSanitizer.Sanitize(locale), LogSanitizer.Sanitize(editorDbref));
		prerenderCache.InvalidatePrefix("/wiki/");
		return NoContent();
	}
}
