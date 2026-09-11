using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// The service that owns visibility filtering and is the only thing allowed to construct a
/// <see cref="LocalizedWikiPage"/>. The draft-leak cases below are the ones most likely to catch a
/// regression that ships unfinished translations to the public, so they are first-class, not an
/// afterthought.
/// </summary>
public class WikiLocalizationServiceTests
{
	/// <summary>
	/// Captures Warning-level messages so the unstamped-row diagnostic can be asserted on. A substitute
	/// would only prove <c>Log</c> was called; the point of that branch is that a human can tell which
	/// page is broken, so the test reads the rendered text.
	/// </summary>
	private sealed class RecordingLogger<T> : ILogger<T>
	{
		public List<string> Warnings { get; } = [];

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(
			LogLevel logLevel, EventId eventId, TState state, Exception? exception,
			Func<TState, Exception?, string> formatter)
		{
			if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
		}
	}

	private static (IWikiService Storage, IWikiLocalizationService Service) Build(string defaultLocale = "en")
	{
		var monitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		monitor.CurrentValue.Returns(TestSharpMushOptions.Create(wikiDefaultLocale: defaultLocale));
		var storage = new InMemoryWikiService(new WikiMarkdigPipeline());
		var resolver = new WikiLocaleResolver(monitor);
		return (storage, new WikiLocalizationService(
			storage, resolver, NullLogger<WikiLocalizationService>.Instance));
	}

	private static async Task<WikiPage> SeedAsync(
		IWikiService storage, string? sourceLocale = "en", string title = "Dragons", string markdown = "en **body**") =>
		(await storage.CreateAsync(title, markdown, "#1", WikiNamespace.Main, "general", sourceLocale)) switch
		{
			WikiPage page => page,
			Error<string> error => throw new InvalidOperationException(error.Value)
		};

	[Test]
	public async Task RequestedSourceLocale_ServesThePageWithNoBanner()
	{
		var (storage, service) = Build();
		var page = await SeedAsync(storage);

		var result = await service.GetLocalizedBySlugAsync("dragons", "general", WikiNamespace.Main, "en", false);

		var localized = await Assert.That(result.Value).IsTypeOf<LocalizedWikiPage>();
		await Assert.That(localized!.Locale).IsEqualTo("en");
		await Assert.That(localized.IsFallback).IsFalse();
		await Assert.That(localized.Title).IsEqualTo(page.Title);
		await Assert.That(localized.MarkdownSource).IsEqualTo("en **body**");
	}

	[Test]
	public async Task PublishedTranslation_IsServedWithNoBanner()
	{
		var (storage, service) = Build();
		var page = await SeedAsync(storage);
		await storage.UpsertTranslationAsync(page.Id, "fr", "Dragons (fr)", "corps fr", "#2", null, published: true, expectedRevisionNumber: null);

		var localized = await Assert.That((await service.GetLocalizedBySlugAsync("dragons", "general", WikiNamespace.Main, "fr", false)).Value).IsTypeOf<LocalizedWikiPage>();

		await Assert.That(localized!.Locale).IsEqualTo("fr");
		await Assert.That(localized.Title).IsEqualTo("Dragons (fr)");
		await Assert.That(localized.MarkdownSource).IsEqualTo("corps fr");
		await Assert.That(localized.IsFallback).IsFalse();
	}

	[Test]
	public async Task UnpublishedTranslation_IsInvisibleToAnOrdinaryReaderWhoGetsTheFallbackAndBanner()
	{
		var (storage, service) = Build();
		var page = await SeedAsync(storage);
		await storage.UpsertTranslationAsync(page.Id, "fr", "Brouillon", "corps brouillon", "#2", null, published: false, expectedRevisionNumber: null);

		var localized = await Assert.That((await service.GetLocalizedBySlugAsync(
			"dragons", "general", WikiNamespace.Main, "fr", includeDrafts: false)).Value).IsTypeOf<LocalizedWikiPage>();

		await Assert.That(localized!.Locale)
			.IsEqualTo("en")
			.Because("a draft translation must fall through exactly as if it did not exist");
		await Assert.That(localized.MarkdownSource).IsEqualTo("en **body**");
		await Assert.That(localized.MarkdownSource).DoesNotContain("brouillon");
		await Assert.That(localized.IsFallback)
			.IsTrue()
			.Because("the reader asked for French and got English, so the notice must show");
	}

	[Test]
	public async Task UnpublishedTranslation_IsVisibleToAnEditorPreviewingTheirOwnDraft()
	{
		var (storage, service) = Build();
		var page = await SeedAsync(storage);
		await storage.UpsertTranslationAsync(page.Id, "fr", "Brouillon", "corps brouillon", "#2", null, published: false, expectedRevisionNumber: null);

		var localized = await Assert.That((await service.GetLocalizedBySlugAsync(
			"dragons", "general", WikiNamespace.Main, "fr", includeDrafts: true)).Value).IsTypeOf<LocalizedWikiPage>();

		await Assert.That(localized!.Locale).IsEqualTo("fr");
		await Assert.That(localized.MarkdownSource).IsEqualTo("corps brouillon");
		await Assert.That(localized.Published)
			.IsFalse()
			.Because("Published is the served row's flag, so the editor can see it is still a draft");
		await Assert.That(localized.IsFallback).IsFalse();
	}

	[Test]
	public async Task GetVisibleTranslationsAsync_HidesDraftsFromOrdinaryReaders()
	{
		var (storage, service) = Build();
		var page = await SeedAsync(storage);
		await storage.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, published: true, expectedRevisionNumber: null);
		await storage.UpsertTranslationAsync(page.Id, "de", "T", "m", "#2", null, published: false, expectedRevisionNumber: null);

		var forReader = await service.GetVisibleTranslationsAsync(page.Id, includeDrafts: false);
		var forEditor = await service.GetVisibleTranslationsAsync(page.Id, includeDrafts: true);

		await Assert.That(forReader.Select(t => t.Locale)).IsEquivalentTo(new[] { "fr" });
		await Assert.That(forEditor.Select(t => t.Locale).Order()).IsEquivalentTo(new[] { "de", "fr" });
	}

	[Test]
	public async Task GetVisibleLocalesAsync_IncludesTheSourceLocaleFirst()
	{
		var (storage, service) = Build();
		var page = await SeedAsync(storage);
		await storage.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, published: true, expectedRevisionNumber: null);
		await storage.UpsertTranslationAsync(page.Id, "de", "T", "m", "#2", null, published: false, expectedRevisionNumber: null);

		var locales = await service.GetVisibleLocalesAsync(page, includeDrafts: false);

		await Assert.That(locales).IsEquivalentTo(new[] { "en", "fr" });
	}

	[Test]
	public async Task StampedSourceLocale_IsNotReinterpretedWhenTheConfiguredDefaultChanges()
	{
		// The regression test for the bug the design fixes. A page authored in French keeps being a French
		// page whatever wiki_default_locale later says, so an admin flipping that setting cannot silently
		// relabel existing content, start rejecting `fr` as "shadowing the source", or change what the
		// revision history means.
		var (storageA, serviceA) = Build("en");
		await SeedAsync(storageA, sourceLocale: "fr");
		var (storageB, serviceB) = Build("de");
		await SeedAsync(storageB, sourceLocale: "fr");

		var onEnglishGame = await Assert.That((await serviceA.GetLocalizedBySlugAsync(
			"dragons", "general", WikiNamespace.Main, "es", false)).Value).IsTypeOf<LocalizedWikiPage>();
		var onGermanGame = await Assert.That((await serviceB.GetLocalizedBySlugAsync(
			"dragons", "general", WikiNamespace.Main, "es", false)).Value).IsTypeOf<LocalizedWikiPage>();

		await Assert.That(onEnglishGame!.Locale).IsEqualTo("fr");
		await Assert.That(onGermanGame!.Locale)
			.IsEqualTo("fr")
			.Because("SourceLocale is materialised once by the migration, never re-derived from configuration");
	}

	[Test]
	public async Task UnstampedSourceLocale_StillRendersAndIsTreatedAsABrokenRow()
	{
		// Reachable only if the Tasks 7-9 backfill has not run. A read can never fail for locale reasons, so
		// the page still renders using the configured default — but this is graceful degradation over a
		// broken row, logged at Warning, NOT a documented meaning for empty. Nothing may depend on it.
		var (storage, service) = Build("fr");
		await SeedAsync(storage, sourceLocale: null);

		var result = await service.GetLocalizedBySlugAsync("dragons", "general", WikiNamespace.Main, "fr", false);

		var localized = await Assert.That(result.Value).IsTypeOf<LocalizedWikiPage>()
			.Because("an unmigrated row must not turn every read of that page into an error");
		await Assert.That(localized!.Locale).IsEqualTo("fr");
	}

	[Test]
	public async Task UnstampedSourceLocale_LogsAWarningNamingThePage()
	{
		// The diagnostic is the whole point of the branch above: a silent substitution would make a database
		// that never ran the backfill indistinguishable from a healthy one.
		var monitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		monitor.CurrentValue.Returns(TestSharpMushOptions.Create(wikiDefaultLocale: "fr"));
		var storage = new InMemoryWikiService(new WikiMarkdigPipeline());
		var logger = new RecordingLogger<WikiLocalizationService>();
		var service = new WikiLocalizationService(storage, new WikiLocaleResolver(monitor), logger);
		var page = await SeedAsync(storage, sourceLocale: null);

		await service.GetLocalizedBySlugAsync("dragons", "general", WikiNamespace.Main, "fr", false);

		await Assert.That(logger.Warnings.Any(w => w.Contains(page.Id, StringComparison.Ordinal)))
			.IsTrue()
			.Because("an unstamped row must be diagnosable, not silently patched over");
	}

	[Test]
	public async Task StampedSourceLocale_LogsNothing()
	{
		var monitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		monitor.CurrentValue.Returns(TestSharpMushOptions.Create());
		var storage = new InMemoryWikiService(new WikiMarkdigPipeline());
		var logger = new RecordingLogger<WikiLocalizationService>();
		var service = new WikiLocalizationService(storage, new WikiLocaleResolver(monitor), logger);
		await SeedAsync(storage);

		await service.GetLocalizedBySlugAsync("dragons", "general", WikiNamespace.Main, "fr", false);

		await Assert.That(logger.Warnings.Count)
			.IsEqualTo(0)
			.Because("a healthy page must not produce migration warnings on every read");
	}

	[Test]
	public async Task RegionalRequest_FindsTheNeutralTranslationWithoutBannering()
	{
		var (storage, service) = Build();
		var page = await SeedAsync(storage);
		await storage.UpsertTranslationAsync(page.Id, "fr", "Dragons (fr)", "corps fr", "#2", null, true, expectedRevisionNumber: null);

		var localized = await Assert.That((await service.GetLocalizedBySlugAsync("dragons", "general", WikiNamespace.Main, "fr-CA", false)).Value).IsTypeOf<LocalizedWikiPage>();

		await Assert.That(localized!.Locale).IsEqualTo("fr");
		await Assert.That(localized.RequestedLocale).IsEqualTo("fr-CA");
		await Assert.That(localized.IsFallback).IsFalse();
	}

	[Test]
	public async Task ExactRegionalTranslation_IsReachableOnAPageAuthoredInTheSameLanguage()
	{
		// The bug Task 4 fixed, guarded at the layer that actually assembles the candidate set: an fr-source
		// page may carry an fr-CA translation, and asking for it by name must not serve the fr source.
		var (storage, service) = Build();
		var page = await SeedAsync(storage, sourceLocale: "fr");
		await storage.UpsertTranslationAsync(page.Id, "fr-CA", "Dragons (fr-CA)", "corps fr-CA", "#2", null, true, expectedRevisionNumber: null);

		var localized = await Assert.That((await service.GetLocalizedBySlugAsync("dragons", "general", WikiNamespace.Main, "fr-CA", false)).Value).IsTypeOf<LocalizedWikiPage>();

		await Assert.That(localized!.Locale).IsEqualTo("fr-CA");
		await Assert.That(localized.MarkdownSource).IsEqualTo("corps fr-CA");
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("not a locale")]
	public async Task MalformedOrAbsentLocale_IsTreatedAsAbsentAndNeverFails(string? requested)
	{
		var (storage, service) = Build();
		await SeedAsync(storage);

		var result = await service.GetLocalizedBySlugAsync(
			"dragons", "general", WikiNamespace.Main, requested, false);

		var localized = await Assert.That(result.Value).IsTypeOf<LocalizedWikiPage>()
			.Because("a read can never fail for locale reasons");
		await Assert.That(localized!.Locale).IsEqualTo("en");
		await Assert.That(localized.IsFallback).IsFalse();
	}

	[Test]
	public async Task MissingPage_StillReturnsNotFound()
	{
		var (_, service) = Build();

		var result = await service.GetLocalizedBySlugAsync("ghost", "general", WikiNamespace.Main, "fr", false);

		await Assert.That(result.Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task LocalizeAllAsync_LocalizesEveryPageAndReturnsOneRowPerPage()
	{
		var (storage, service) = Build();
		var first = await SeedAsync(storage, title: "Alpha", markdown: "a");
		var second = await SeedAsync(storage, title: "Beta", markdown: "b");
		await storage.UpsertTranslationAsync(first.Id, "fr", "Alpha (fr)", "a-fr", "#2", null, true, expectedRevisionNumber: null);

		var localized = await service.LocalizeAllAsync([first, second], "fr", includeDrafts: false);

		await Assert.That(localized.Count)
			.IsEqualTo(2)
			.Because("listings must still return one row per page, not N rows per locale");
		await Assert.That(localized.Single(p => p.Page.Id == first.Id).Title).IsEqualTo("Alpha (fr)");
		await Assert.That(localized.Single(p => p.Page.Id == second.Id).Title).IsEqualTo("Beta");
		await Assert.That(localized.Single(p => p.Page.Id == second.Id).IsFallback).IsTrue();
	}

	[Test]
	public async Task LocalizeAllAsync_DoesNotLeakADraftTitleIntoAListing()
	{
		var (storage, service) = Build();
		var page = await SeedAsync(storage, title: "Alpha", markdown: "a");
		await storage.UpsertTranslationAsync(page.Id, "fr", "Alpha (brouillon)", "a-fr", "#2", null, published: false, expectedRevisionNumber: null);

		var localized = await service.LocalizeAllAsync([page], "fr", includeDrafts: false);

		await Assert.That(localized.Single().Title).IsEqualTo("Alpha");
	}

	[Test]
	public async Task ResolvedContentNeverLeaksOntoThePage()
	{
		var (storage, service) = Build();
		var page = await SeedAsync(storage);
		await storage.UpsertTranslationAsync(page.Id, "fr", "Dragons (fr)", "corps fr", "#2", null, true, expectedRevisionNumber: null);

		var localized = await Assert.That((await service.GetLocalizedBySlugAsync("dragons", "general", WikiNamespace.Main, "fr", false)).Value).IsTypeOf<LocalizedWikiPage>();

		await Assert.That(localized!.Page.Title).IsEqualTo("Dragons");
		await Assert.That(localized.Page.MarkdownSource)
			.IsEqualTo("en **body**")
			.Because("Page carries identity and inherited metadata only — never content");
		await Assert.That(localized.Page.Category).IsEqualTo("general");
	}
}
