using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Helpers;
using SharpMUSH.Server.Middleware;
using SharpMUSH.Server.Services;
using System.Net;
using SharpMUSH.Server.Authentication;
using System.Text;
using System.Text.Json;
using System.Web;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// REST API for wiki page data and bot-facing pre-rendered HTML.
/// Routes:
///   GET    /api/wiki/ns/{ns}/{category}/{slug} — JSON page data (canonical, all clients)
///   GET    /api/wiki/character/{name}  — character namespace alias
///   GET    /api/wiki/recent           — recently updated pages
///   GET    /api/wiki/ns/{ns}           — list pages in a namespace
///   GET    /api/wiki/{slug}/revisions — revision history (newest first)
///   GET    /api/wiki/{slug}/revisions/{n} — single revision snapshot
///   GET    /api/wiki/{slug}/translations — locales this reader may read the page in
///   PUT    /api/wiki/{slug}/translations/{locale} — create/update one locale (authenticated)
///   DELETE /api/wiki/{slug}/translations/{locale} — remove one locale (authenticated)
///   POST   /api/wiki                  — create page (authenticated)
///   PUT    /api/wiki/{slug}            — update page (authenticated)
///   DELETE /api/wiki/{slug}             — delete page (Wizard+)
///   PUT    /api/wiki/{slug}/protection  — set protection flag (Wizard+)
///   POST   /api/wiki/{slug}/rollback    — restore an earlier revision (authenticated)
///   POST   /api/wiki/exists             — batch page-existence check (redlinks)
///   GET    /api/wiki/pages            — paginated listing of all pages (X-Total-Count header)
///   GET    /api/wiki/category/{cat}   — pages in a category
///   GET    /api/wiki/tag/{tag}        — pages carrying a tag
///   PUT    /api/wiki/{slug}/metadata  — set category/tags/published (authenticated)
///   POST   /api/wiki/batch/protect    — batch protection change (Wizard+)
///   POST   /api/wiki/batch/delete     — batch deletion (Wizard+)
///   POST   /api/wiki/invalidate-cache — evict pre-render cache entries after an edit
/// </summary>
[ApiController]
[Route("api/wiki")]
public class WikiController(
	IWikiService wikiService,
	IWikiLocalizationService localization,
	IPrerenderCacheService prerenderCache,
	ILogger<WikiController> logger) : ControllerBase
{
	/// <summary>Page data returned by the API. Includes MarkdownSource so the editor can round-trip.</summary>
	public record WikiPageDto(
		string Id,
		string Slug,
		string Title,
		string Namespace,
		string MarkdownSource,
		string RenderedHtml,
		string PlainText,
		DateTimeOffset CreatedAt,
		DateTimeOffset UpdatedAt,
		bool IsProtected,
		int RevisionNumber,
		string? Category,
		IReadOnlyList<string> Tags,
		bool Published)
	{
		// Localization fields are init-only with defaults so that ToDto(WikiPage) — used by every
		// endpoint that has not been localized yet — keeps compiling and keeps its current shape.

		/// <summary>The locale actually served.</summary>
		public string Locale { get; init; } = string.Empty;

		/// <summary>The normalised locale the reader asked for.</summary>
		public string RequestedLocale { get; init; } = string.Empty;

		/// <summary>True when a different language was served than requested — drives the reader's notice.</summary>
		public bool IsFallback { get; init; }

		/// <summary>Locales this reader can actually read the page in, source locale first.</summary>
		public IReadOnlyList<string> AvailableLocales { get; init; } = [];
	}

	/// <summary>A translation without its body — enough for locale lists and hreflang.</summary>
	public record WikiTranslationSummaryDto(
		string Locale,
		string Title,
		bool Published,
		DateTimeOffset UpdatedAt,
		int RevisionNumber);

	/// <summary>Request body for creating or updating one locale's translation of a page.</summary>
	/// <param name="ExpectedRevisionNumber">
	/// The <c>RevisionNumber</c> the editor loaded, for optimistic concurrency. Null means create-only.
	/// A stale value is answered with 409 and must not be retried — see <see cref="PutTranslation"/>.
	/// </param>
	public record UpsertTranslationRequest(
		string Title,
		string Markdown,
		string? EditSummary,
		bool Published,
		int? ExpectedRevisionNumber);

	/// <summary>A single revision snapshot. MarkdownSource is the full page body at that revision.</summary>
	public record WikiRevisionDto(
		int RevisionNumber,
		string EditorDbref,
		DateTimeOffset Timestamp,
		string? EditSummary,
		string MarkdownSource);

	private static WikiPageDto ToDto(WikiPage p) => new(
		p.Id, p.Slug, p.Title, p.Namespace, p.MarkdownSource, p.RenderedHtml, p.PlainText,
		p.CreatedAt, p.UpdatedAt, p.IsProtected, p.RevisionNumber,
		p.Category, p.Tags, p.Published);

	private static WikiPageDto ToDto(LocalizedWikiPage p, IReadOnlyList<string> availableLocales) => new(
		p.Page.Id, p.Page.Slug, p.Title, p.Page.Namespace, p.MarkdownSource, p.RenderedHtml, p.PlainText,
		p.Page.CreatedAt, p.Page.UpdatedAt, p.Page.IsProtected, p.RevisionNumber,
		p.Page.Category, p.Page.Tags, p.Published)
	{
		Locale = p.Locale,
		RequestedLocale = p.RequestedLocale,
		IsFallback = p.IsFallback,
		AvailableLocales = availableLocales,
	};

	private static WikiRevisionDto ToDto(WikiRevision r) => new(
		r.RevisionNumber, r.EditorDbref, r.Timestamp, r.EditSummary, r.MarkdownSource);

	private static WikiTranslationSummaryDto ToDto(WikiTranslationSummary t) =>
		new(t.Locale, t.Title, t.Published, t.UpdatedAt, t.RevisionNumber);

	private static WikiNamespace ParseNamespace(string? ns) =>
		Enum.TryParse<WikiNamespace>(ns, ignoreCase: true, out var result) ? result : WikiNamespace.Main;

	private static WikiNamespace? ParseOptionalNamespace(string? ns) =>
		string.IsNullOrWhiteSpace(ns) ? null
		: Enum.TryParse<WikiNamespace>(ns, ignoreCase: true, out var result) ? result : WikiNamespace.Main;

	/// <summary>
	/// Parses a wiki page reference into its (namespace, category, slug) identity. Accepts
	/// "ns/category/slug" (canonical), "ns/slug" (category defaults to general), or "slug"
	/// (main namespace, general category).
	/// </summary>
	private static (WikiNamespace Ns, string Category, string Slug) ParseRef(string reference)
	{
		var parts = reference.Split('/');
		return parts.Length switch
		{
			>= 3 => (ParseNamespace(parts[0]), parts[1], string.Join('/', parts[2..])),
			2 => (ParseNamespace(parts[0]), WikiHelpers.DefaultCategory, parts[1]),
			_ => (WikiNamespace.Main, WikiHelpers.DefaultCategory, reference)
		};
	}

	/// <summary>True when the caller may see unpublished (draft) pages — i.e. holds the
	/// <see cref="PortalPermission.WikiRead"/> scope. Anonymous callers and accounts without the
	/// scope only see Published pages. (Player and above are granted wiki.read by default, so the
	/// historical "any logged-in user sees drafts" behavior is preserved out of the box.)</summary>
	private bool CanSeeUnpublished => User.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiRead);

	/// <summary>
	/// True when the caller may see unpublished <em>translations</em>: they can already see drafts, or they
	/// hold the edit scope and so may be previewing their own translation at <c>?lang=</c>.
	/// </summary>
	private bool IncludeDrafts =>
		CanSeeUnpublished || User.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiEdit);

	/// <summary>
	/// The caller's character dbref (the acting/primary character, from the <c>character_dbref</c>
	/// claim). Never defaults to a privileged dbref: a missing claim means we cannot attribute the
	/// action, so callers must reject the request rather than silently acting as God (#1).
	/// </summary>
	private string? CallerDbref => User.GetActingCharacterDbref();

	/// <summary>True when the caller is the original author of <paramref name="page"/>. Authors
	/// always see their own drafts even without the <see cref="PortalPermission.WikiRead"/> scope.</summary>
	private bool IsAuthor(WikiPage page) =>
		CallerDbref is { Length: > 0 } me && string.Equals(page.AuthorDbref, me, StringComparison.Ordinal);

	/// <summary>True when the caller may view <paramref name="page"/>: it is published, the caller
	/// can see unpublished pages (wiki.read), or the caller authored it.</summary>
	private bool CanSee(WikiPage page) => page.Published || CanSeeUnpublished || IsAuthor(page);

	/// <summary>Filters out unpublished (draft) pages the caller may not see (not published, no
	/// wiki.read scope, and not their own authored draft).</summary>
	private IEnumerable<WikiPage> FilterVisible(IEnumerable<WikiPage> pages) => pages.Where(CanSee);

	/// <summary>
	/// GET /api/wiki/ns/{namespace}/{category}/{slug}?lang=fr
	/// Returns JSON page data for a page identified by (namespace, category, slug), resolved into the
	/// reader's locale, or 404 when the page doesn't exist. This is the canonical page route.
	/// <c>lang</c> is advisory: a malformed or unknown tag is treated as absent and falls to the
	/// configured default rather than producing a 400.
	/// </summary>
	[HttpGet("ns/{ns}/{category}/{slug}")]
	public async Task<IActionResult> GetPage(string ns, string category, string slug, [FromQuery] string? lang = null)
	{
		var result = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (result.IsT1 || !CanSee(result.AsT0)) return NotFound();

		return Ok(await LocalizedDtoAsync(result.AsT0, lang));
	}

	/// <summary>
	/// GET /api/wiki/character/{name}?lang=fr
	/// Resolves the /character/{name} alias to the Character namespace wiki page (default category),
	/// resolved into the reader's locale.
	/// </summary>
	[HttpGet("character/{name}")]
	public async Task<IActionResult> GetCharacterPage(string name, [FromQuery] string? lang = null)
	{
		var result = await wikiService.GetBySlugAsync(name, WikiHelpers.DefaultCategory, WikiNamespace.Character);
		if (result.IsT1 || !CanSee(result.AsT0)) return NotFound();

		return Ok(await LocalizedDtoAsync(result.AsT0, lang));
	}

	/// <summary>
	/// Resolves a page into the reader's locale and packages it with the locales that reader may see.
	/// A requested tag that does not resolve is logged at Debug — it is a client-side hint, not an error.
	/// </summary>
	private async Task<WikiPageDto> LocalizedDtoAsync(WikiPage page, string? lang)
	{
		var includeDrafts = IncludeDrafts;
		var localized = await localization.LocalizeAsync(page, lang, includeDrafts);
		var available = await localization.GetVisibleLocalesAsync(page, includeDrafts);

		// Read path: a bad tag is a client-side hint, never a 400. NormalizeLocaleOrEmpty is the permissive
		// form for exactly this reason — the OneOf-returning NormalizeLocale belongs at write boundaries.
		if (!string.IsNullOrWhiteSpace(lang) && WikiHelpers.NormalizeLocaleOrEmpty(lang).Length == 0)
			logger.LogDebug("Unrecognised wiki lang tag ignored: {Lang}", LogSanitizer.Sanitize(lang));

		return ToDto(localized, available);
	}

	/// <summary>
	/// Localizes a listing into the reader's locale, one DTO per page. <c>AvailableLocales</c> is left
	/// empty here on purpose: a listing does not drive language chips or hreflang, and loading every
	/// page's translation set to fill it would be N extra queries for data nothing reads.
	/// </summary>
	/// <remarks>
	/// <see cref="FilterVisible"/> runs first and is not optional — the page-level draft gate is a
	/// different rule from translation visibility, and a localized listing that dropped it would leak
	/// unpublished pages while every locale assertion stayed green.
	/// </remarks>
	private async Task<IEnumerable<WikiPageDto>> LocalizedListAsync(IEnumerable<WikiPage> pages, string? lang)
	{
		var visible = FilterVisible(pages).ToList();
		var localized = await localization.LocalizeAllAsync(visible, lang, IncludeDrafts);
		return localized.Select(p => ToDto(p, []));
	}

	/// <summary>
	/// GET /api/wiki/recent?count=20&amp;lang=fr
	/// Returns recently updated pages with titles resolved into the reader's locale, one row per page.
	/// </summary>
	[HttpGet("recent")]
	public async Task<IActionResult> GetRecentChanges([FromQuery] int count = 20, [FromQuery] string? lang = null)
	{
		var pages = await wikiService.GetRecentChangesAsync(count);
		return Ok(await LocalizedListAsync(pages, lang));
	}

	/// <summary>
	/// GET /api/wiki/ns/{namespace}?skip=0&amp;take=50&amp;lang=fr
	/// Lists pages within a namespace, ordered by title, with pagination and localized titles.
	/// </summary>
	[HttpGet("ns/{ns}")]
	public async Task<IActionResult> ListNamespacePages(
		string ns, [FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] string? lang = null)
	{
		var pages = await wikiService.GetByNamespaceAsync(ParseNamespace(ns), skip, take);
		return Ok(await LocalizedListAsync(pages, lang));
	}

	/// <summary>
	/// GET /api/wiki/pages?skip=0&amp;take=50&amp;ns=&amp;lang=fr
	/// Paginated listing of all pages (optionally restricted to a namespace), with localized titles.
	/// The X-Total-Count response header carries the unpaginated page count.
	/// Anonymous callers only see published pages.
	/// </summary>
	[HttpGet("pages")]
	public async Task<IActionResult> ListAllPages(
		[FromQuery] int skip = 0, [FromQuery] int take = 50,
		[FromQuery] string? ns = null, [FromQuery] string? lang = null)
	{
		var nsFilter = ParseOptionalNamespace(ns);
		var pages = await wikiService.GetAllPagesAsync(skip, take, nsFilter);
		// CountPagesAsync includes drafts, so only expose the total to authenticated callers.
		// Emitting it for anonymous users would leak how many unpublished pages exist and
		// would not match the published-only collection they receive.
		if (CanSeeUnpublished)
			Response.Headers["X-Total-Count"] = (await wikiService.CountPagesAsync(nsFilter)).ToString();
		return Ok(await LocalizedListAsync(pages, lang));
	}

	/// <summary>
	/// GET /api/wiki/category/{category}?skip=0&amp;take=50&amp;lang=fr
	/// Lists pages in a category with localized titles. Anonymous callers only see published pages.
	/// </summary>
	[HttpGet("category/{category}")]
	public async Task<IActionResult> ListCategoryPages(
		string category, [FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] string? lang = null)
	{
		var pages = await wikiService.GetByCategoryAsync(category, skip, take);
		return Ok(await LocalizedListAsync(pages, lang));
	}

	/// <summary>
	/// GET /api/wiki/tag/{tag}?skip=0&amp;take=50&amp;lang=fr
	/// Lists pages carrying a tag with localized titles. Anonymous callers only see published pages.
	/// </summary>
	[HttpGet("tag/{tag}")]
	public async Task<IActionResult> ListTagPages(
		string tag, [FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] string? lang = null)
	{
		var pages = await wikiService.GetByTagAsync(tag, skip, take);
		return Ok(await LocalizedListAsync(pages, lang));
	}

	/// <summary>
	/// GET /api/wiki/{slug}/revisions?skip=&amp;take=&amp;ns=&amp;category=&amp;lang=
	/// Revision history, newest first. Omitting <c>lang</c> (or naming the page's source locale) returns
	/// the source-locale stream, which is exactly what this route returned before translations existed.
	/// </summary>
	[HttpGet("{slug}/revisions")]
	public async Task<IActionResult> GetRevisions(
		string slug, [FromQuery] int skip = 0, [FromQuery] int take = 20,
		[FromQuery] string? ns = null, [FromQuery] string? category = null,
		[FromQuery] string? lang = null)
	{
		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1) return NotFound();
		// Mirror GetPage: drafts (and their history) are hidden from anonymous callers.
		if (!CanSee(lookup.AsT0)) return NotFound();

		var page = lookup.AsT0;
		var localized = await localization.LocalizeAsync(page, lang, IncludeDrafts);

		// The source page's revisions are stored with an empty Locale; a translation's carry its tag.
		// SourceLocaleOf, not a local re-derivation: SourceLocale is materialised once and there is exactly
		// one accessor for it, so a controller cannot start disagreeing with the resolver about what
		// language a page was authored in.
		var stream = string.Equals(
			localized.Locale, localization.SourceLocaleOf(page), StringComparison.OrdinalIgnoreCase)
			? string.Empty
			: localized.Locale;

		var revisions = await wikiService.GetRevisionsForLocaleAsync(page.Id, stream, skip, take);
		return Ok(revisions.Select(ToDto));
	}

	/// <summary>
	/// GET /api/wiki/{slug}/translations?ns=&amp;category=
	/// Lists the translations of a page that this reader may see. Drafts are omitted for readers without
	/// the edit scope, so the language chips and <c>hreflang</c> never advertise a page nobody can open.
	/// </summary>
	[HttpGet("{slug}/translations")]
	public async Task<IActionResult> GetTranslations(
		string slug, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1 || !CanSee(lookup.AsT0)) return NotFound();

		var summaries = await localization.GetVisibleTranslationsAsync(lookup.AsT0.Id, IncludeDrafts);
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

		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1) return NotFound();

		if (lookup.AsT0.IsProtected && !User.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiAdmin))
			return Forbid();

		var result = await wikiService.UpsertTranslationAsync(
			lookup.AsT0.Id, locale, request.Title, request.Markdown,
			editorDbref, request.EditSummary, request.Published, request.ExpectedRevisionNumber);

		return result.Match<IActionResult>(
			translation =>
			{
				logger.LogInformation(
					"Wiki translation saved: slug={Slug} locale={Locale} rev={Rev} by={Editor}",
					LogSanitizer.Sanitize(slug), LogSanitizer.Sanitize(translation.Locale),
					translation.RevisionNumber, LogSanitizer.Sanitize(editorDbref));
				prerenderCache.InvalidatePrefix("/wiki/");
				return Ok(ToDto(new WikiTranslationSummary(
					translation.Locale, translation.Title, translation.Published,
					translation.UpdatedAt, translation.RevisionNumber)));
			},
			// A revision conflict is 409, not 400: the request was well-formed and the client's correct
			// response is to reload, which is a different instruction from "your body was invalid". The
			// endpoint does not retry — retrying would re-apply this caller's stale markdown over the
			// winner's, which is the loss expectedRevisionNumber exists to prevent.
			err => IsRevisionConflict(err.Value)
				? Conflict(new { error = err.Value, reload = true })
				: BadRequest(new { error = err.Value }));
	}

	/// <summary>
	/// True when an upsert error is an optimistic-concurrency conflict rather than a bad request.
	/// </summary>
	/// <remarks>
	/// Matched on the storage layer's wording because <c>OneOf&lt;T, Error&lt;string&gt;&gt;</c> carries no
	/// error code. All four implementations (in-memory plus the three backends) phrase these two cases
	/// identically, which is what makes the match reliable rather than lucky. If a third conflict phrasing
	/// is ever added, promote this to a typed error rather than growing the string list — this is the one
	/// place that would silently start answering 400.
	/// </remarks>
	private static bool IsRevisionConflict(string error) =>
		error.Contains("changed while you were editing", StringComparison.OrdinalIgnoreCase)
		|| error.Contains("already exists for page", StringComparison.OrdinalIgnoreCase);

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

		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1) return NotFound();

		if (lookup.AsT0.IsProtected && !User.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiAdmin))
			return Forbid();

		var result = await wikiService.DeleteTranslationAsync(lookup.AsT0.Id, locale, editorDbref);

		return result.Match<IActionResult>(
			_ =>
			{
				logger.LogInformation(
					"Wiki translation deleted: slug={Slug} locale={Locale} by={Editor}",
					LogSanitizer.Sanitize(slug), LogSanitizer.Sanitize(locale), LogSanitizer.Sanitize(editorDbref));
				prerenderCache.InvalidatePrefix("/wiki/");
				return NoContent();
			},
			_ => NotFound());
	}

	/// <summary>
	/// GET /api/wiki/{slug}/revisions/{number}
	/// Returns a single revision snapshot, including its full markdown body.
	/// </summary>
	[HttpGet("{slug}/revisions/{number:int}")]
	public async Task<IActionResult> GetRevision(string slug, int number, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1) return NotFound();
		// Mirror GetPage: drafts (and their history) are hidden from anonymous callers.
		if (!CanSee(lookup.AsT0)) return NotFound();

		var result = await wikiService.GetRevisionAsync(lookup.AsT0.Id, number);
		return result.Match<IActionResult>(
			revision => Ok(ToDto(revision)),
			_ => NotFound());
	}

	/// <summary>Request body for creating a new wiki page. Category is part of identity and is fixed at create.</summary>
	public record CreatePageRequest(string Title, string Markdown, string? Namespace, string? Category);

	/// <summary>Request body for updating an existing wiki page.</summary>
	public record UpdatePageRequest(string Markdown, string? EditSummary);

	/// <summary>Request body for setting page protection.</summary>
	public record SetProtectionRequest(bool IsProtected);

	/// <summary>
	/// POST /api/wiki
	/// Creates a new wiki page. The slug is derived from the title.
	/// </summary>
	[HttpPost]
	[Authorize(Policy = PortalPermission.WikiCreate)]
	public async Task<IActionResult> CreatePage([FromBody] CreatePageRequest request)
	{
		var authorDbref = CallerDbref;
		if (string.IsNullOrEmpty(authorDbref))
			return Unauthorized("Missing character identity.");
		var ns = ParseNamespace(request.Namespace);
		// SourceLocale is materialised at creation. The configured default affects new pages and fallback
		// resolution only; it never reinterprets a page that already exists.
		var result = await wikiService.CreateAsync(
			request.Title, request.Markdown, authorDbref, ns, request.Category, localization.DefaultLocale);
		return result.Match<IActionResult>(
			page =>
			{
				logger.LogInformation("Wiki page created: slug={Slug} ns={Ns} category={Category} by={Author}",
					LogSanitizer.Sanitize(page.Slug), ns, LogSanitizer.Sanitize(page.Category), LogSanitizer.Sanitize(authorDbref));
				return CreatedAtAction(nameof(GetPage),
					new { ns = page.Namespace, category = page.Category, slug = page.Slug }, ToDto(page));
			},
			err => Conflict(new { error = err.Value }));
	}

	/// <summary>
	/// PUT /api/wiki/{slug}
	/// Updates an existing wiki page's markdown content, identified by its slug.
	/// Using slug (not the internal DB ID) avoids encoded-slash routing issues with
	/// ArangoDB-style IDs (e.g. "node_wiki_pages/1532") which contain a literal '/'.
	/// </summary>
	[HttpPut("{slug}")]
	[Authorize(Policy = PortalPermission.WikiEdit)]
	public async Task<IActionResult> UpdatePage(string slug, [FromBody] UpdatePageRequest request, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		var editorDbref = CallerDbref;
		if (string.IsNullOrEmpty(editorDbref))
			return Unauthorized("Missing character identity.");
		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1) return NotFound();

		// Protected pages may only be edited by Wizard-level users.
		if (lookup.AsT0.IsProtected && !User.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiAdmin))
			return Forbid();

		var id = lookup.AsT0.Id;
		var result = await wikiService.UpdateAsync(id, request.Markdown, editorDbref, request.EditSummary);
		return result.Match<IActionResult>(
			page =>
			{
				logger.LogInformation("Wiki page updated: slug={Slug} rev={Rev} by={Editor}", LogSanitizer.Sanitize(slug), page.RevisionNumber, LogSanitizer.Sanitize(editorDbref));
				prerenderCache.InvalidatePrefix($"/wiki/");
				return Ok(ToDto(page));
			},
			_ => NotFound());
	}

	/// <summary>Request body for rolling a page back to an earlier revision.</summary>
	public record RollbackRequest(int RevisionNumber);

	/// <summary>
	/// POST /api/wiki/{slug}/rollback
	/// Restores the page body from an earlier revision snapshot. The restore is a
	/// normal edit — it creates a NEW revision rather than rewriting history, so
	/// a rollback can itself be rolled back.
	/// </summary>
	[HttpPost("{slug}/rollback")]
	[Authorize(Policy = PortalPermission.WikiEdit)]
	public async Task<IActionResult> RollbackPage(string slug, [FromBody] RollbackRequest request, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		var editorDbref = CallerDbref;
		if (string.IsNullOrEmpty(editorDbref))
			return Unauthorized("Missing character identity.");
		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1) return NotFound();

		var page = lookup.AsT0;
		if (page.IsProtected && !User.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiAdmin))
			return Forbid();

		var revisionLookup = await wikiService.GetRevisionAsync(page.Id, request.RevisionNumber);
		if (revisionLookup.IsT1) return NotFound();

		var result = await wikiService.UpdateAsync(
			page.Id, revisionLookup.AsT0.MarkdownSource, editorDbref,
			$"rollback to r{request.RevisionNumber}");
		return result.Match<IActionResult>(
			updated =>
			{
				logger.LogInformation("Wiki page rolled back: slug={Slug} to r{Target} (now r{Rev}) by={Editor}",
					LogSanitizer.Sanitize(slug), request.RevisionNumber, updated.RevisionNumber, LogSanitizer.Sanitize(editorDbref));
				prerenderCache.InvalidatePrefix($"/wiki/");
				return Ok(ToDto(updated));
			},
			_ => NotFound());
	}

	/// <summary>Request body for the batch existence check. Refs use URL-path form:
	/// "ns/category/slug" (canonical), "ns/slug" (general category), or "slug" (main/general).</summary>
	public record ExistsRequest(string[] Refs);

	/// <summary>
	/// POST /api/wiki/exists
	/// Batch existence check used by the client to mark redlinks at view time.
	/// Returns a map of each requested ref to whether the page exists (and is
	/// visible to the caller — drafts count as missing for anonymous callers).
	/// </summary>
	[HttpPost("exists")]
	[AllowAnonymous]
	public async Task<IActionResult> CheckExists([FromBody] ExistsRequest request)
	{
		const int maxRefs = 200;
		var result = new Dictionary<string, bool>(StringComparer.Ordinal);

		foreach (var reference in request.Refs.Distinct(StringComparer.Ordinal).Take(maxRefs))
		{
			var (ns, category, slug) = ParseRef(reference);
			var lookup = await wikiService.GetBySlugAsync(slug, category, ns);
			result[reference] = lookup.IsT0 && CanSee(lookup.AsT0);
		}

		return Ok(result);
	}

	/// <summary>
	/// DELETE /api/wiki/{slug}
	/// Deletes a wiki page and all its revisions, identified by slug.
	/// </summary>
	[HttpDelete("{slug}")]
	[Authorize(Policy = PortalPermission.WikiDelete)]
	public async Task<IActionResult> DeletePage(string slug, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		var editorDbref = CallerDbref;
		if (string.IsNullOrEmpty(editorDbref))
			return Unauthorized("Missing character identity.");
		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1) return NotFound();

		var id = lookup.AsT0.Id;
		var result = await wikiService.DeleteAsync(id, editorDbref);
		return result.Match<IActionResult>(
			_ =>
			{
				logger.LogInformation("Wiki page deleted: slug={Slug} by={Editor}", LogSanitizer.Sanitize(slug), LogSanitizer.Sanitize(editorDbref));
				prerenderCache.InvalidatePrefix($"/wiki/");
				return NoContent();
			},
			_ => NotFound());
	}

	/// <summary>
	/// PUT /api/wiki/{slug}/protection
	/// Sets or clears the protection flag on a wiki page, identified by slug.
	/// </summary>
	[HttpPut("{slug}/protection")]
	[Authorize(Policy = PortalPermission.WikiAdmin)]
	public async Task<IActionResult> SetProtection(string slug, [FromBody] SetProtectionRequest request, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1) return NotFound();

		var id = lookup.AsT0.Id;
		var result = await wikiService.SetProtectionAsync(id, request.IsProtected);
		return result.Match<IActionResult>(
			_ => Ok(),
			_ => NotFound());
	}

	/// <summary>Request body for setting page metadata.</summary>
	public record SetMetadataRequest(string? Category, string[] Tags, bool Published);

	/// <summary>
	/// PUT /api/wiki/{slug}/metadata
	/// Sets the category, tags and published flag on a page, identified by slug.
	/// Does not create a content revision.
	/// </summary>
	[HttpPut("{slug}/metadata")]
	[Authorize(Policy = PortalPermission.WikiEdit)]
	public async Task<IActionResult> SetMetadata(string slug, [FromBody] SetMetadataRequest request, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1) return NotFound();

		// Protected pages may only be retagged/(un)published by Wizard-level users,
		// mirroring the edit restriction in UpdatePage.
		if (lookup.AsT0.IsProtected && !User.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiAdmin))
			return Forbid();

		var result = await wikiService.SetMetadataAsync(
			lookup.AsT0.Id, request.Category, request.Tags ?? [], request.Published);
		return result.Match<IActionResult>(
			page =>
			{
				logger.LogInformation("Wiki page metadata updated: slug={Slug} category={Category} published={Published}",
					LogSanitizer.Sanitize(slug), LogSanitizer.Sanitize(page.Category), page.Published);
				prerenderCache.InvalidatePrefix("/wiki/");
				return Ok(ToDto(page));
			},
			_ => NotFound());
	}

	/// <summary>Request body for batch protection changes. Refs use "ns/category/slug" form.</summary>
	public record BatchProtectRequest(string[] Refs, bool IsProtected);

	/// <summary>Request body for batch deletion. Refs use "ns/category/slug" form.</summary>
	public record BatchDeleteRequest(string[] Refs);

	/// <summary>Per-slug outcome of a batch operation.</summary>
	public record BatchResult(IReadOnlyList<string> Succeeded, IReadOnlyList<string> Failed);

	/// <summary>
	/// POST /api/wiki/batch/protect
	/// Sets or clears the protection flag on multiple pages at once.
	/// </summary>
	[HttpPost("batch/protect")]
	[Authorize(Policy = PortalPermission.WikiAdmin)]
	public async Task<IActionResult> BatchProtect([FromBody] BatchProtectRequest request)
	{
		var succeeded = new List<string>();
		var failed = new List<string>();

		foreach (var reference in request.Refs ?? [])
		{
			var (ns, category, slug) = ParseRef(reference);
			var lookup = await wikiService.GetBySlugAsync(slug, category, ns);
			if (lookup.IsT1)
			{
				failed.Add(reference);
				continue;
			}

			var result = await wikiService.SetProtectionAsync(lookup.AsT0.Id, request.IsProtected);
			(result.IsT0 ? succeeded : failed).Add(reference);
		}

		logger.LogInformation("Wiki batch protect: protected={Protected} ok={Ok} failed={Failed}",
			request.IsProtected, succeeded.Count, failed.Count);
		return Ok(new BatchResult(succeeded, failed));
	}

	/// <summary>
	/// POST /api/wiki/batch/delete
	/// Deletes multiple pages (and their revisions) at once.
	/// </summary>
	[HttpPost("batch/delete")]
	[Authorize(Policy = PortalPermission.WikiAdmin)]
	public async Task<IActionResult> BatchDelete([FromBody] BatchDeleteRequest request)
	{
		var editorDbref = CallerDbref;
		if (string.IsNullOrEmpty(editorDbref))
			return Unauthorized("Missing character identity.");
		var succeeded = new List<string>();
		var failed = new List<string>();

		foreach (var reference in request.Refs ?? [])
		{
			var (ns, category, slug) = ParseRef(reference);
			var lookup = await wikiService.GetBySlugAsync(slug, category, ns);
			if (lookup.IsT1)
			{
				failed.Add(reference);
				continue;
			}

			var result = await wikiService.DeleteAsync(lookup.AsT0.Id, editorDbref);
			(result.IsT0 ? succeeded : failed).Add(reference);
		}

		prerenderCache.InvalidatePrefix("/wiki/");
		logger.LogInformation("Wiki batch delete: by={Editor} ok={Ok} failed={Failed}",
			LogSanitizer.Sanitize(editorDbref), succeeded.Count, failed.Count);
		return Ok(new BatchResult(succeeded, failed));
	}

	public record InvalidateCacheRequest(string? Path, string? Prefix);

	/// <summary>
	/// POST /api/wiki/invalidate-cache
	/// Evicts one or more pre-render cache entries.
	/// Called by the wiki edit handler after a page is saved.
	/// </summary>
	[HttpPost("invalidate-cache")]
	[Authorize(Policy = PortalPermission.WikiAdmin)]
	public IActionResult InvalidateCache([FromBody] InvalidateCacheRequest request)
	{
		if (!string.IsNullOrWhiteSpace(request.Path))
			prerenderCache.Invalidate(request.Path);

		if (!string.IsNullOrWhiteSpace(request.Prefix))
			prerenderCache.InvalidatePrefix(request.Prefix);

		logger.LogInformation("Pre-render cache invalidated: path={Path} prefix={Prefix}",
			LogSanitizer.Sanitize(request.Path), LogSanitizer.Sanitize(request.Prefix));

		return Ok();
	}

	/// <summary>
	/// Generates a minimal static HTML page for bot consumption.
	/// Includes OpenGraph meta tags and the rendered page content.
	/// </summary>
	public static string GeneratePrerenderHtml(WikiPage page, string canonicalUrl, string siteName = "SharpMUSH")
	{
		var title = HttpUtility.HtmlEncode($"{page.Title} - {siteName} Wiki");
		var ogTitle = HttpUtility.HtmlEncode($"{page.Title} - {siteName} Wiki");
		var ogDesc = HttpUtility.HtmlEncode(
			page.PlainText.Length > 200 ? page.PlainText[..200] + "…" : page.PlainText);
		var ogUrl = HttpUtility.HtmlEncode(canonicalUrl);
		var canonical = HttpUtility.HtmlEncode(canonicalUrl);

		var sb = new StringBuilder();
		sb.AppendLine("<!DOCTYPE html>");
		sb.AppendLine("<html lang=\"en\">");
		sb.AppendLine("<head>");
		sb.AppendLine($"  <meta charset=\"utf-8\" />");
		sb.AppendLine($"  <title>{title}</title>");
		sb.AppendLine($"  <link rel=\"canonical\" href=\"{canonical}\" />");
		sb.AppendLine($"  <meta property=\"og:title\" content=\"{ogTitle}\" />");
		sb.AppendLine($"  <meta property=\"og:description\" content=\"{ogDesc}\" />");
		sb.AppendLine($"  <meta property=\"og:type\" content=\"article\" />");
		sb.AppendLine($"  <meta property=\"og:url\" content=\"{ogUrl}\" />");
		sb.AppendLine($"  <script type=\"application/ld+json\">{BuildArticleJsonLd(page, canonicalUrl)}</script>");
		sb.AppendLine("</head>");
		sb.AppendLine("<body>");
		sb.AppendLine($"  <h1>{HttpUtility.HtmlEncode(page.Title)}</h1>");
		sb.AppendLine($"  {page.RenderedHtml}");
		sb.AppendLine("</body>");
		sb.AppendLine("</html>");
		return sb.ToString();
	}

	/// <summary>
	/// Builds a schema.org Article JSON-LD block for the pre-rendered page.
	/// Serializing the whole object via System.Text.Json guarantees safe escaping
	/// of titles/URLs containing quotes or angle brackets.
	/// </summary>
	private static string BuildArticleJsonLd(WikiPage page, string canonicalUrl)
	{
		var jsonLd = new Dictionary<string, object>
		{
			["@context"] = "https://schema.org",
			["@type"] = "Article",
			["headline"] = page.Title,
			["datePublished"] = page.CreatedAt.ToString("O"),
			["dateModified"] = page.UpdatedAt.ToString("O"),
			["url"] = canonicalUrl,
		};
		return JsonSerializer.Serialize(jsonLd);
	}

	/// <summary>
	/// Generates a minimal static HTML page for a character profile bot response.
	/// </summary>
	public static string GenerateCharacterPrerenderHtml(WikiPage page, string canonicalUrl, string siteName = "SharpMUSH")
	{
		var title = HttpUtility.HtmlEncode($"{page.Title} - {siteName}");
		var ogTitle = HttpUtility.HtmlEncode(page.Title);
		var ogDesc = HttpUtility.HtmlEncode(
			page.PlainText.Length > 200 ? page.PlainText[..200] + "…" : page.PlainText);
		var ogUrl = HttpUtility.HtmlEncode(canonicalUrl);
		var canonical = HttpUtility.HtmlEncode(canonicalUrl);

		var sb = new StringBuilder();
		sb.AppendLine("<!DOCTYPE html>");
		sb.AppendLine("<html lang=\"en\">");
		sb.AppendLine("<head>");
		sb.AppendLine($"  <meta charset=\"utf-8\" />");
		sb.AppendLine($"  <title>{title}</title>");
		sb.AppendLine($"  <link rel=\"canonical\" href=\"{canonical}\" />");
		sb.AppendLine($"  <meta property=\"og:title\" content=\"{ogTitle}\" />");
		sb.AppendLine($"  <meta property=\"og:description\" content=\"{ogDesc}\" />");
		sb.AppendLine($"  <meta property=\"og:type\" content=\"profile\" />");
		sb.AppendLine($"  <meta property=\"og:url\" content=\"{ogUrl}\" />");
		sb.AppendLine("</head>");
		sb.AppendLine("<body>");
		sb.AppendLine($"  <h1>{HttpUtility.HtmlEncode(page.Title)}</h1>");
		sb.AppendLine($"  {page.RenderedHtml}");
		sb.AppendLine("</body>");
		sb.AppendLine("</html>");
		return sb.ToString();
	}
}
