using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// One in-game wiki search hit: the page, and the locale whose text actually matched.
/// </summary>
/// <remarks>
/// The locale is carried rather than recomputed because it cannot be recovered from the page: the whole
/// point of scanning translations is that the needle may appear in no text the page itself holds. Callers
/// that only need references (<c>wikisearch()</c>) ignore it; <c>@wiki/search</c> renders it as the same
/// <c>[xx]</c> marker <c>@wiki/view</c> uses for a fallback.
/// </remarks>
/// <param name="Page">The page that matched. Identity and metadata; never localized content.</param>
/// <param name="Locale">
/// The locale of the text that matched — the page's source locale for a source hit, the translation's own
/// for a translation hit. When several locales matched, the reader's own wins; see
/// <c>ListWiki.SearchPagesAsync</c>.
/// </param>
internal sealed record WikiSearchMatch(WikiPage Page, string Locale);
