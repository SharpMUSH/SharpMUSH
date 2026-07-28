using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Resolves wiki content for a reader's locale. Controllers, middleware and softcode inject this rather
/// than <see cref="IWikiService"/> when they want content a human will read.
/// </summary>
/// <remarks>
/// This is the only type allowed to construct a <see cref="LocalizedWikiPage"/>, which is what gives the
/// "resolved content lives on the wrapper, never on the page" invariant exactly one enforcement point.
/// It is also where the visibility decision lives: every method takes <c>includeDrafts</c> and filters the
/// candidate set before <see cref="IWikiLocaleResolver.Resolve"/> ever sees it, so the resolver stays
/// permission-blind and an unpublished translation is unreachable rather than merely un-rendered.
/// </remarks>
public interface IWikiLocalizationService
{
	/// <summary>The normalised, game-wide configured fallback locale (<c>Wiki.DefaultLocale</c>).</summary>
	string DefaultLocale { get; }

	/// <summary>
	/// The locale a page was authored in: its materialised <see cref="WikiPage.SourceLocale"/>, canonicalised.
	/// </summary>
	/// <remarks>
	/// Exists so no caller re-derives this. The field is stamped once by <c>Migration_AddWikiTranslations</c>
	/// and by every create path, and is immutable thereafter — the configured default affects new pages and
	/// fallback resolution, never the interpretation of an existing one. An empty value means the backfill has
	/// not run: this method logs a warning and returns <see cref="DefaultLocale"/> so the page still renders,
	/// which is degradation over a broken row rather than a meaning callers may rely on.
	/// </remarks>
	string SourceLocaleOf(WikiPage page);

	/// <summary>
	/// Looks a page up by identity and resolves it into <paramref name="requestedLocale"/>.
	/// Returns <c>NotFound</c> only when the page itself does not exist — never for locale reasons.
	/// </summary>
	/// <param name="includeDrafts">True when the caller may see unpublished translations, i.e. may edit
	/// the page. Ordinary readers pass false and fall back as though drafts were absent.</param>
	Task<OneOf<LocalizedWikiPage, NotFound>> GetLocalizedBySlugAsync(
		string slug, string? category, WikiNamespace ns, string? requestedLocale, bool includeDrafts);

	/// <summary>Resolves an already-loaded page. Never fails.</summary>
	Task<LocalizedWikiPage> LocalizeAsync(WikiPage page, string? requestedLocale, bool includeDrafts);

	/// <summary>
	/// Resolves a whole listing. Returns exactly one row per input page — localized listings show
	/// localized titles, not N rows per locale.
	/// </summary>
	Task<IReadOnlyList<LocalizedWikiPage>> LocalizeAllAsync(
		IReadOnlyList<WikiPage> pages, string? requestedLocale, bool includeDrafts);

	/// <summary>Translations of a page this reader may see, ordered by locale.</summary>
	Task<IReadOnlyList<WikiTranslationSummary>> GetVisibleTranslationsAsync(string pageId, bool includeDrafts);

	/// <summary>
	/// Every locale this reader can actually read the page in: the page's source locale first, then each
	/// visible translation's locale. Drives the language chip row and <c>hreflang</c>.
	/// </summary>
	Task<IReadOnlyList<string>> GetVisibleLocalesAsync(WikiPage page, bool includeDrafts);
}
