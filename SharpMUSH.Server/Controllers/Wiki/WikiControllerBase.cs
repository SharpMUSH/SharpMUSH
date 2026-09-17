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
/// What every <c>/api/wiki</c> controller needs to answer a request: who the caller is, what they
/// may see, how a page reference parses, and how a page becomes a DTO in the reader's locale.
/// </summary>
/// <remarks>
/// The visibility rules are the reason this is a base class and not a helper bag. <c>CanSee</c>,
/// <c>FilterVisible</c> and <c>IncludeDrafts</c> read the request principal, they are the page-level
/// draft gate, and a listing endpoint that forgot one of them would leak unpublished pages while
/// every other assertion about it stayed green. One declaration, inherited, cannot be forgotten.
/// </remarks>
public abstract class WikiControllerBase(
	IWikiService wikiService,
	IWikiLocalizationService localization,
	ILogger logger) : ControllerBase
{
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
	/// Parses a wiki page reference into its (namespace, category, slug) identity. Accepts
	/// "ns/category/slug" (canonical), "ns/slug" (category defaults to general), or "slug"
	/// (main namespace, general category).
	/// </summary>
	protected static (WikiNamespace Ns, string Category, string Slug) ParseRef(string reference)
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
	protected bool CanSeeUnpublished => User.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiRead);

	/// <summary>
	/// True when the caller may see unpublished <em>translations</em>: they can already see drafts, or they
	/// hold the edit scope and so may be previewing their own translation at <c>?lang=</c>.
	/// </summary>
	protected bool IncludeDrafts =>
		CanSeeUnpublished || User.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiEdit);

	/// <summary>
	/// The caller's character dbref (the acting/primary character, from the <c>character_dbref</c>
	/// claim). Never defaults to a privileged dbref: a missing claim means we cannot attribute the
	/// action, so callers must reject the request rather than silently acting as God (#1).
	/// </summary>
	protected string? CallerDbref => User.GetActingCharacter()?.ToString();

	/// <summary>True when the caller is the original author of <paramref name="page"/>. Authors
	/// always see their own drafts even without the <see cref="PortalPermission.WikiRead"/> scope.</summary>
	protected bool IsAuthor(WikiPage page) =>
		CallerDbref is { Length: > 0 } me && string.Equals(page.AuthorDbref, me, StringComparison.Ordinal);

	/// <summary>True when the caller may view <paramref name="page"/>: it is published, the caller
	/// can see unpublished pages (wiki.read), or the caller authored it.</summary>
	protected bool CanSee(WikiPage page) => page.Published || CanSeeUnpublished || IsAuthor(page);

	/// <summary>Filters out unpublished (draft) pages the caller may not see (not published, no
	/// wiki.read scope, and not their own authored draft).</summary>
	protected IEnumerable<WikiPage> FilterVisible(IEnumerable<WikiPage> pages) => pages.Where(CanSee);

	protected static WikiPageDto ToDto(WikiPage p) => new(
		p.Id, p.Slug, p.Title, p.Namespace, p.MarkdownSource, p.RenderedHtml, p.PlainText,
		p.CreatedAt, p.UpdatedAt, p.IsProtected, p.RevisionNumber,
		p.Category, p.Tags, p.Published);

	protected static WikiPageDto ToDto(LocalizedWikiPage p, IReadOnlyList<string> availableLocales) => new(
		p.Page.Id, p.Page.Slug, p.Title, p.Page.Namespace, p.MarkdownSource, p.RenderedHtml, p.PlainText,
		p.Page.CreatedAt, p.Page.UpdatedAt, p.Page.IsProtected, p.RevisionNumber,
		p.Page.Category, p.Page.Tags, p.Published)
	{
		Locale = p.Locale,
		RequestedLocale = p.RequestedLocale,
		IsFallback = p.IsFallback,
		AvailableLocales = availableLocales,
	};

	protected static WikiRevisionDto ToDto(WikiRevision r) => new(
		r.RevisionNumber, r.EditorDbref, r.Timestamp, r.EditSummary, r.MarkdownSource);

	protected static WikiTranslationSummaryDto ToDto(WikiTranslationSummary t) =>
		new(t.Locale, t.Title, t.Published, t.UpdatedAt, t.RevisionNumber);

	/// <summary>
	/// Resolves a page into the reader's locale and packages it with the locales that reader may see.
	/// A requested tag that does not resolve is logged at Debug — it is a client-side hint, not an error.
	/// </summary>
	protected async Task<WikiPageDto> LocalizedDtoAsync(WikiPage page, string? lang)
	{
		var includeDrafts = IncludeDrafts;
		var localized = await Localization.LocalizeAsync(page, lang, includeDrafts);
		var available = await Localization.GetVisibleLocalesAsync(page, includeDrafts);

		// Read path: a bad tag is a client-side hint, never a 400. NormalizeLocaleOrEmpty is the permissive
		// form for exactly this reason — the Result-returning NormalizeLocale belongs at write boundaries.
		if (!string.IsNullOrWhiteSpace(lang) && WikiHelpers.NormalizeLocaleOrEmpty(lang).Length == 0)
			Logger.LogDebug("Unrecognised wiki lang tag ignored: {Lang}", LogSanitizer.Sanitize(lang));

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
	protected async Task<IEnumerable<WikiPageDto>> LocalizedListAsync(IEnumerable<WikiPage> pages, string? lang)
	{
		var visible = FilterVisible(pages).ToList();
		var localized = await Localization.LocalizeAllAsync(visible, lang, IncludeDrafts);
		return localized.Select(p => ToDto(p, []));
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
		var localized = await Localization.LocalizeAsync(page, lang, IncludeDrafts);

		return string.Equals(
			localized.Locale, Localization.SourceLocaleOf(page), StringComparison.OrdinalIgnoreCase)
			? string.Empty
			: localized.Locale;
	}
}
