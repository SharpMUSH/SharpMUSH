namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>The outcome of resolving a reader's requested locale against a page's available content.</summary>
/// <param name="Locale">The locale to serve. Always a canonical, parseable tag.</param>
/// <param name="IsFallback">True when the served locale is a different language from the requested one.</param>
public sealed record LocaleResolution(string Locale, bool IsFallback);

/// <summary>
/// The one place wiki locale-fallback rules live. No database, no HTTP, and no permission awareness —
/// callers hand it a candidate set they have already filtered by visibility, which is what keeps draft
/// translations from leaking without teaching the rules about an auth graph.
/// </summary>
public interface IWikiLocaleResolver
{
	/// <summary>The normalised, game-wide configured fallback locale (<c>Wiki.DefaultLocale</c>).</summary>
	string DefaultLocale { get; }

	/// <summary>
	/// Normalises a caller-supplied locale tag, substituting <see cref="DefaultLocale"/> when it is
	/// absent, blank or unparseable. Never throws and never returns empty.
	/// </summary>
	string NormalizeRequested(string? requested);

	/// <summary>
	/// Resolves which locale to serve, in order:
	/// <list type="number">
	///   <item>Normalise <paramref name="requested"/> — null, blank or unparseable becomes <see cref="DefaultLocale"/>.</item>
	///   <item>The page's own <paramref name="sourceLocale"/>, if it is the requested language.</item>
	///   <item>Exact match against <paramref name="available"/>, case-insensitive.</item>
	///   <item>Neutral-language match: <c>fr-CA</c> finds an <c>fr</c> translation and vice versa.</item>
	///   <item><see cref="DefaultLocale"/>, if a translation exists for it.</item>
	///   <item><paramref name="sourceLocale"/> — the <c>WikiPage</c> row itself, which always exists.</item>
	/// </list>
	/// </summary>
	/// <param name="requested">The reader's locale, unvalidated.</param>
	/// <param name="sourceLocale">
	/// The page's stamped <c>SourceLocale</c>: a non-empty canonical tag. This is a precondition, not
	/// something this method normalises — the configured default must never be substituted for it, or
	/// changing <c>wiki_default_locale</c> would reinterpret the authored locale of every existing page.
	/// <c>IWikiLocalizationService</c> is the single place that copes with an unstamped row.
	/// </param>
	/// <param name="available">Locales with content the caller has decided this reader may see.</param>
	LocaleResolution Resolve(string? requested, string sourceLocale, IReadOnlyCollection<string> available);
}
