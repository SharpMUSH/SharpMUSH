using Microsoft.Extensions.Logging;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc cref="IWikiLocalizationService"/>
public sealed class WikiLocalizationService(
	IWikiService wikiService,
	IWikiLocaleResolver resolver,
	ILogger<WikiLocalizationService> logger) : IWikiLocalizationService
{
	public string DefaultLocale => resolver.DefaultLocale;

	public async Task<OneOf<LocalizedWikiPage, NotFound>> GetLocalizedBySlugAsync(
		string slug, string? category, WikiNamespace ns, string? requestedLocale, bool includeDrafts)
	{
		var lookup = await wikiService.GetBySlugAsync(slug, category, ns);
		if (lookup.IsT1) return new NotFound();

		return await LocalizeAsync(lookup.AsT0, requestedLocale, includeDrafts);
	}

	public async Task<LocalizedWikiPage> LocalizeAsync(WikiPage page, string? requestedLocale, bool includeDrafts)
	{
		var visible = await GetVisibleTranslationsAsync(page.Id, includeDrafts);
		return await BuildAsync(page, requestedLocale, visible);
	}

	public async Task<IReadOnlyList<LocalizedWikiPage>> LocalizeAllAsync(
		IReadOnlyList<WikiPage> pages, string? requestedLocale, bool includeDrafts)
	{
		var results = new List<LocalizedWikiPage>(pages.Count);
		foreach (var page in pages)
		{
			results.Add(await LocalizeAsync(page, requestedLocale, includeDrafts));
		}

		return results.AsReadOnly();
	}

	public async Task<IReadOnlyList<WikiTranslationSummary>> GetVisibleTranslationsAsync(
		string pageId, bool includeDrafts)
	{
		var all = await wikiService.GetTranslationsAsync(pageId);
		return all
			.Where(t => includeDrafts || t.Published)
			.OrderBy(t => t.Locale, StringComparer.OrdinalIgnoreCase)
			.ToList()
			.AsReadOnly();
	}

	public async Task<IReadOnlyList<string>> GetVisibleLocalesAsync(WikiPage page, bool includeDrafts)
	{
		var visible = await GetVisibleTranslationsAsync(page.Id, includeDrafts);
		var source = SourceLocaleOf(page);

		return new[] { source }
			.Concat(visible.Select(t => t.Locale))
			.Where(l => l.Length > 0)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList()
			.AsReadOnly();
	}

	/// <inheritdoc/>
	public string SourceLocaleOf(WikiPage page)
	{
		// Read the materialised value straight through. Substituting the configured default for a stamped
		// value would mean an admin changing wiki_default_locale silently relabels the authored locale of
		// every existing page — an English page starts claiming to be French, UpsertTranslationAsync begins
		// rejecting `fr` as "shadowing the source", and revision history changes meaning, with no migration
		// and nothing to alert on.
		var normalized = WikiHelpers.NormalizeLocaleOrEmpty(page.SourceLocale);
		if (normalized.Length > 0) return normalized;

		// Unreachable once Migration_AddWikiTranslations has run, which is why it is a Warning rather than a
		// branch anything is allowed to depend on. A read can never fail for locale reasons, so the page
		// still renders — but the row is broken, and pre-production the fix is to re-run migrations or wipe
		// and reseed, not to make this substitution part of the design.
		logger.LogWarning(
			"Wiki page {PageId} ({Slug}) has no SourceLocale. The Migration_AddWikiTranslations backfill has "
			+ "not run on this database; serving it as '{DefaultLocale}' for this read only.",
			page.Id, page.Slug, resolver.DefaultLocale);

		return resolver.DefaultLocale;
	}

	/// <summary>
	/// The single construction site for <see cref="LocalizedWikiPage"/>. The requested tag is normalised
	/// here and the source tag comes from <see cref="SourceLocaleOf"/>; together with every write boundary
	/// rejecting an unparseable locale, that is what guarantees the <c>CultureInfo</c> calls in
	/// <c>IsFallback</c> cannot throw.
	/// </summary>
	/// <remarks>
	/// Only the winning translation's body is loaded. Resolving against the bodyless summaries first keeps a
	/// listing of N pages at two queries per page instead of one per translation, and the resolver sees the
	/// same candidate set either way because <paramref name="visible"/> is already visibility-filtered.
	/// </remarks>
	private async Task<LocalizedWikiPage> BuildAsync(
		WikiPage page, string? requestedLocale, IReadOnlyList<WikiTranslationSummary> visible)
	{
		var source = SourceLocaleOf(page);
		var requested = resolver.NormalizeRequested(requestedLocale);
		var resolution = resolver.Resolve(requested, source, visible.Select(t => t.Locale).ToList());

		var winner = visible.FirstOrDefault(
			t => string.Equals(t.Locale, resolution.Locale, StringComparison.OrdinalIgnoreCase));

		// The source row is what the resolver falls back to, and it always exists.
		if (winner is null) return FromSource(page, source, requested);

		// A translation deleted between the summary listing and this read leaves nothing to serve for that
		// locale, and a read can never fail for locale reasons — so degrade to the source rather than throw.
		var row = await wikiService.GetTranslationAsync(page.Id, winner.Locale);
		if (row.IsT1) return FromSource(page, source, requested);

		var served = row.AsT0;
		return new LocalizedWikiPage(
			Page: page,
			Locale: served.Locale,
			RequestedLocale: requested,
			Title: served.Title,
			MarkdownSource: served.MarkdownSource,
			RenderedHtml: served.RenderedHtml,
			PlainText: served.PlainText,
			Published: served.Published,
			RevisionNumber: served.RevisionNumber);
	}

	private static LocalizedWikiPage FromSource(WikiPage page, string source, string requested) =>
		new(
			Page: page,
			Locale: source,
			RequestedLocale: requested,
			Title: page.Title,
			MarkdownSource: page.MarkdownSource,
			RenderedHtml: page.RenderedHtml,
			PlainText: page.PlainText,
			Published: page.Published,
			RevisionNumber: page.RevisionNumber);
}
