using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc cref="IWikiLocaleResolver"/>
public sealed class WikiLocaleResolver(IOptionsMonitor<SharpMUSHOptions> options) : IWikiLocaleResolver
{
	public string DefaultLocale
	{
		get
		{
			// Last resort when Wiki.DefaultLocale itself is unparseable. ValidateSharpOptions rejects that
			// at startup, so this branch exists only so a hand-edited stored config degrades to a readable
			// page rather than throwing inside a render.
			var configured = WikiHelpers.NormalizeLocaleOrEmpty(options.CurrentValue.Wiki.DefaultLocale);
			return configured.Length == 0 ? WikiOptions.DefaultLocaleFallback : configured;
		}
	}

	public string NormalizeRequested(string? requested)
	{
		// Deliberately the permissive form: a reader's bad ?lang= becomes the default, never an error.
		var normalized = WikiHelpers.NormalizeLocaleOrEmpty(requested);
		return normalized.Length == 0 ? DefaultLocale : normalized;
	}

	public LocaleResolution Resolve(string? requested, string sourceLocale, IReadOnlyCollection<string> available)
	{
		var want = NormalizeRequested(requested);

		// sourceLocale is the page's materialised SourceLocale and is authoritative. Canonicalise the
		// casing, but do NOT substitute DefaultLocale for an empty value: that re-derivation is what let
		// a change to wiki_default_locale silently relabel every pre-existing page. Task 10 handles the
		// unstamped-row case once, loudly.
		var source = WikiHelpers.NormalizeLocaleOrEmpty(sourceLocale);

		// The source row always exists, so prefer it when the request names it *exactly*. That also makes a
		// stale translation row shadowing the source unreachable rather than authoritative.
		//
		// Exactly, not same-language: the write boundary rejects only locale == SourceLocale, so an
		// fr-source page may legitimately carry an fr-CA translation. A language-level test here would
		// swallow the request for it and serve the source instead, making that row unreachable for the
		// only reader who could ever want it.
		if (string.Equals(want, source, StringComparison.OrdinalIgnoreCase))
			return new LocaleResolution(source, IsFallback: false);

		if (Match(available, c => string.Equals(c, want, StringComparison.OrdinalIgnoreCase)) is { } exact)
			return new LocaleResolution(exact, IsFallback: false);

		// Same language as the source, with no exact translation to prefer: serving the fr source to an
		// fr-CA reader is a language match, so it is not a fallback.
		if (WikiHelpers.SameLanguage(want, source))
			return new LocaleResolution(source, IsFallback: false);

		if (Match(available, c => WikiHelpers.SameLanguage(c, want)) is { } neutral)
			return new LocaleResolution(neutral, IsFallback: false);

		if (Match(available, c => WikiHelpers.SameLanguage(c, DefaultLocale)) is { } fallbackDefault)
			return new LocaleResolution(fallbackDefault, IsFallback: true);

		return new LocaleResolution(source, IsFallback: true);
	}

	/// <summary>
	/// First candidate satisfying <paramref name="predicate"/>, ordered so the result does not depend on
	/// the caller's collection order: exact-length tags before regional variants, then alphabetical.
	/// </summary>
	private static string? Match(IReadOnlyCollection<string> available, Func<string, bool> predicate) =>
		available
			.Where(c => WikiHelpers.NormalizeLocaleOrEmpty(c).Length > 0)
			.Where(predicate)
			.OrderBy(c => c.Length)
			.ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
			.Select(WikiHelpers.NormalizeLocaleOrEmpty)
			.FirstOrDefault();
}
