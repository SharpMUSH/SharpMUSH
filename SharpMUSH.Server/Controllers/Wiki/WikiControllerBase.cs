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
/// What every <c>/api/wiki</c> controller needs to answer a request: who the caller is, what they
/// may see, how a page reference parses, and how a page becomes a DTO in the reader's locale.
/// </summary>
/// <remarks>
/// The visibility rules are the reason this is a base class and not a helper bag. <c>CanSeeAsync</c>,
/// <c>FilterVisibleAsync</c>, <c>VisibilityAsync</c> and <c>RefusalAsync</c> read the request principal and ask
/// <see cref="IWikiAccessService"/>; they are the page-level gates, and a listing endpoint that forgot one of them would leak unpublished pages while
/// every other assertion about it stayed green. One declaration, inherited, cannot be forgotten.
/// </remarks>
public abstract class WikiControllerBase(
	IWikiService wikiService,
	IWikiLocalizationService localization,
	IWikiAccessService access,
	IWikiNameResolver names,
	ILogger logger) : ControllerBase
{
	private WikiReader? _reader;

	/// <summary>Who may read, edit and delete which page; every gate in these controllers asks it.</summary>
	protected IWikiAccessService Access { get; } = access;

	/// <summary>Resolves the author and editor dbrefs the store keeps to player names for the DTOs.</summary>
	protected IWikiNameResolver Names { get; } = names;

	/// <summary>Page storage. A property rather than a captured parameter so derived controllers,
	/// which pass the same instance down, do not each capture a second copy of it.</summary>
	protected IWikiService Wiki { get; } = wikiService;

	/// <summary>Locale resolution and translation visibility.</summary>
	protected IWikiLocalizationService Localization { get; } = localization;

	/// <summary>The derived controller's own category, so a log line names the endpoint it came from.</summary>
	protected ILogger Logger { get; } = logger;

	protected static WikiNamespace ParseNamespace(string? ns) =>
		Enum.TryParse<WikiNamespace>(ns, ignoreCase: true, out var result) ? result : WikiNamespace.Main;

	protected static WikiNamespace? ParseOptionalNamespace(string? ns) =>
		string.IsNullOrWhiteSpace(ns) ? null
		: Enum.TryParse<WikiNamespace>(ns, ignoreCase: true, out var result) ? result : WikiNamespace.Main;

	/// <summary>
	/// Parses a wiki page reference into its (namespace, slug) identity. Accepts "ns/slug" (canonical) or
	/// "slug" (main namespace).
	/// </summary>
	protected static (WikiNamespace Ns, string Slug) ParseRef(string reference)
	{
		var parts = reference.Split('/', 2);
		return parts.Length == 2
			? (ParseNamespace(parts[0]), parts[1])
			: (WikiNamespace.Main, reference);
	}

	/// <summary>
	/// The caller as a wiki reader: the permission claims the request was authenticated with (rebuilt from
	/// the account's roles on every request) and the acting character's dbref; an anonymous caller holds
	/// what the <c>everyone</c> role grants. Read once per request.
	/// </summary>
	protected async Task<WikiReader> ReaderAsync()
		=> _reader ??= User.Identity?.IsAuthenticated == true
			? WikiReader.From(User.FindAll(PortalPermission.ClaimType).Select(claim => claim.Value), CallerDbref)
			: await Access.AnonymousAsync(HttpContext.RequestAborted);

	/// <summary>
	/// True when the caller may see unpublished <em>translations</em>: they hold <c>wiki.drafts</c>, as for
	/// draft pages. Holding <c>wiki.edit</c> is not enough, or every player would read every draft translation.
	/// </summary>
	protected async Task<bool> IncludeDraftsAsync() => (await ReaderAsync()).Has(PortalPermission.WikiDrafts);

	/// <summary>
	/// The caller's character dbref (the acting/primary character, from the <c>character_dbref</c>
	/// claim). Never defaults to a privileged dbref: a missing claim means we cannot attribute the
	/// action, so callers must reject the request rather than silently acting as God (#1).
	/// </summary>
	protected string? CallerDbref => User.GetActingCharacter()?.ToString();

	/// <summary>
	/// The pages the caller may view: published ones, every draft with wiki.drafts, the drafts the caller
	/// authored, less what the namespace, category and page read requirements keep from them. Listings hand
	/// it to the store, which applies it before paging, so a page of a listing is a page of rows this caller
	/// may see.
	/// </summary>
	protected async Task<WikiVisibility> VisibilityAsync() => await Access.VisibilityAsync(await ReaderAsync());

	/// <summary>True when the caller may view <paramref name="page"/>.</summary>
	protected async Task<bool> CanSeeAsync(WikiPage page) => await Access.CanSeeAsync(await ReaderAsync(), page);

	/// <summary>
	/// Null when the caller may take <paramref name="action"/> on <paramref name="page"/>; otherwise the
	/// answer to give: 404 when they may not see it at all (so a refusal does not disclose the page), else 403.
	/// </summary>
	protected async Task<IActionResult?> RefusalAsync(WikiPage page, WikiAction action)
	{
		var reader = await ReaderAsync();
		if (!await Access.CanSeeAsync(reader, page)) return NotFound();
		return (await Access.DecideAsync(reader, page, action)).Allowed ? null : Forbid();
	}

	/// <summary>
	/// Gives the categories the caller just filed a page in a category page titled as they typed them
	/// (<see cref="IWikiService.NameCategoriesAsync"/>), when the caller may create pages in the category
	/// namespace. Otherwise the categories still hold the page and show the name their key spells.
	/// </summary>
	protected async Task NameCategoriesAsync(IEnumerable<string> names, IEnumerable<string> alreadyFiled, string authorDbref)
	{
		var typed = names.ToList();
		if (typed.Count == 0
				|| !(await Access.DecideCreateAsync(await ReaderAsync(), WikiHelpers.NamespaceName(WikiNamespace.Category), [])).Allowed)
			return;
		await Wiki.NameCategoriesAsync(typed, alreadyFiled, authorDbref, Localization.DefaultLocale);
	}

	/// <summary>Filters out the pages the caller may not see.</summary>
	protected async Task<List<WikiPage>> FilterVisibleAsync(IEnumerable<WikiPage> pages)
	{
		var visibility = await VisibilityAsync();
		return pages.Where(visibility.Admits).ToList();
	}

	protected static WikiPageDto ToDto(WikiPage p, bool restricted) => new(
		p.Id, p.Slug, p.Title, p.Namespace, p.MarkdownSource, p.RenderedHtml, p.PlainText,
		p.CreatedAt, p.UpdatedAt, restricted, p.RevisionNumber,
		p.Categories, p.Published);

	protected static WikiPageDto ToDto(LocalizedWikiPage p, IReadOnlyList<string> availableLocales, bool restricted) => new(
		p.Page.Id, p.Page.Slug, p.Title, p.Page.Namespace, p.MarkdownSource, p.RenderedHtml, p.PlainText,
		p.Page.CreatedAt, p.UpdatedAt, restricted, p.RevisionNumber,
		p.Page.Categories, p.Published)
	{
		Locale = p.Locale,
		RequestedLocale = p.RequestedLocale,
		IsFallback = p.IsFallback,
		AvailableLocales = availableLocales,
	};

	protected static WikiRevisionDto ToDto(WikiRevision r) => new(
		r.RevisionNumber, r.EditorDbref, r.Timestamp, r.EditSummary, r.MarkdownSource);

	/// <summary>
	/// The page DTO with the facts the D1 banner needs (the last editor's name and the first image) and
	/// what the caller may do with the page.
	/// </summary>
	protected async Task<WikiPageDto> ToDtoAsync(WikiPage p) => ToDto(p, await IsRestrictedAsync(p)) with
	{
		LastEditedBy = await Names.NameOfAsync(p.LastEditorDbref, HttpContext.RequestAborted),
		Image = WikiImages.FirstImageUrl(p.RenderedHtml),
		Access = await AccessDtoAsync(p),
	};

	protected async Task<WikiPageDto> ToDtoAsync(LocalizedWikiPage p, IReadOnlyList<string> availableLocales)
		=> ToDto(p, availableLocales, await IsRestrictedAsync(p.Page)) with
		{
			LastEditedBy = await Names.NameOfAsync(p.LastEditorDbref, HttpContext.RequestAborted),
			Image = WikiImages.FirstImageUrl(p.RenderedHtml),
			Access = await AccessDtoAsync(p.Page),
		};

	/// <summary>
	/// True when any requirement applies to the page, its own or one it inherits, so the page never claims
	/// anyone may edit it while its namespace or a category says otherwise.
	/// </summary>
	protected async Task<bool> IsRestrictedAsync(WikiPage page)
	{
		var requirements = await Access.RequirementsAsync();
		return requirements.HasPageRules(page.Id) || requirements.Required(page, WikiAction.Delete).Count > 0;
	}

	private async Task<WikiAccessDto> AccessDtoAsync(WikiPage page)
	{
		var access = await Access.ForPageAsync(await ReaderAsync(), page);
		return new WikiAccessDto(access.Read, access.Edit, access.Delete, access.Manage);
	}

	protected async Task<WikiRevisionDto> ToDtoAsync(WikiRevision r) => ToDto(r) with
	{
		EditorName = await Names.NameOfAsync(r.EditorDbref, HttpContext.RequestAborted),
	};

	protected async Task<List<WikiRevisionDto>> ToDtosAsync(IEnumerable<WikiRevision> revisions)
	{
		var list = new List<WikiRevisionDto>();
		foreach (var revision in revisions)
		{
			list.Add(await ToDtoAsync(revision));
		}

		return list;
	}

	protected static WikiTranslationSummaryDto ToDto(WikiTranslationSummary t) =>
		new(t.Locale, t.Title, t.Published, t.UpdatedAt, t.RevisionNumber);

	/// <summary>
	/// Resolves a page into the reader's locale and packages it with the locales that reader may see.
	/// A requested tag that does not resolve is logged at Debug — it is a client-side hint, not an error.
	/// </summary>
	protected async Task<WikiPageDto> LocalizedDtoAsync(WikiPage page, string? lang)
	{
		var includeDrafts = await IncludeDraftsAsync();
		var localized = await Localization.LocalizeAsync(page, lang, includeDrafts);
		var available = await Localization.GetVisibleLocalesAsync(page, includeDrafts);

		// Read path: a bad tag is a client-side hint, never a 400. NormalizeLocaleOrEmpty is the permissive
		// form for exactly this reason — the Result-returning NormalizeLocale belongs at write boundaries.
		if (!string.IsNullOrWhiteSpace(lang) && WikiHelpers.NormalizeLocaleOrEmpty(lang).Length == 0)
			Logger.LogDebug("Unrecognised wiki lang tag ignored: {Lang}", LogSanitizer.Sanitize(lang));

		return await ToDtoAsync(localized, available);
	}

	/// <summary>The most rows one listing request returns, whatever <c>take</c> or <c>count</c> asks for.</summary>
	protected const int MaxListTake = 500;

	/// <summary>Clamps a listing's paging arguments: no negative skip, and at most <see cref="MaxListTake"/> rows.</summary>
	protected static (int Skip, int Take) ClampPage(int skip, int take) =>
		(Math.Max(0, skip), Math.Clamp(take, 0, MaxListTake));

	/// <summary>
	/// Localizes a listing into the reader's locale, one body-less summary per page. Locales a page is
	/// available in are not loaded: a listing does not drive language chips or hreflang, and loading every
	/// page's translation set to fill it would be N extra queries for data nothing reads.
	/// </summary>
	/// <remarks>
	/// <see cref="FilterVisibleAsync"/> runs first and is not optional — the page-level gate is a
	/// different rule from translation visibility, and a localized listing that dropped it would leak
	/// unpublished or restricted pages while every locale assertion stayed green.
	/// </remarks>
	protected async Task<IEnumerable<WikiPageSummaryDto>> LocalizedListAsync(IEnumerable<WikiPage> pages, string? lang)
	{
		var visible = await FilterVisibleAsync(pages);
		var localized = await Localization.LocalizeAllAsync(visible, lang, await IncludeDraftsAsync());
		var requirements = await Access.RequirementsAsync();
		var dtos = new List<WikiPageSummaryDto>();
		foreach (var page in localized)
		{
			dtos.Add(new WikiPageSummaryDto(
				page.Page.Id, page.Page.Slug, page.Title, page.Page.Namespace, page.UpdatedAt, requirements.HasPageRules(page.Page.Id),
				page.RevisionNumber, page.Page.Categories, page.Published, page.Locale, page.IsFallback,
				WikiImages.FirstImageUrl(page.RenderedHtml),
				await Names.NameOfAsync(page.LastEditorDbref, HttpContext.RequestAborted)));
		}

		return dtos;
	}

	/// <summary>
	/// Resolves which revision stream a <c>lang</c> query names: <see cref="string.Empty"/> for the source
	/// page's own stream, or the translation's locale tag.
	/// </summary>
	/// <remarks>
	/// Shared by <see cref="GetRevisions"/> and <see cref="GetRevision"/> so the list and the per-revision
	/// fetch can never disagree about which stream a request is in — that disagreement is precisely how a
	/// history list of French revisions ends up diffing English bodies.
	/// <para>
	/// Resolution goes through <c>LocalizeAsync</c>, so a draft translation the caller may not see falls
	/// back to the source stream rather than leaking its prose through the diff view, and
	/// <c>SourceLocaleOf</c> rather than a local re-derivation keeps the controller from disagreeing with
	/// the resolver about what language a page was authored in.
	/// </para>
	/// </remarks>
	protected async Task<string> ResolveRevisionStreamAsync(WikiPage page, string? lang)
	{
		var localized = await Localization.LocalizeAsync(page, lang, await IncludeDraftsAsync());

		return string.Equals(
			localized.Locale, Localization.SourceLocaleOf(page), StringComparison.OrdinalIgnoreCase)
			? string.Empty
			: localized.Locale;
	}
}
