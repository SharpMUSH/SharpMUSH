using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// The prerendered HTML is what crawlers and link unfurlers see. A translated page that still declares
/// <c>&lt;html lang="en"&gt;</c> or omits its alternates is invisible as a translation no matter how good
/// it is.
/// </summary>
/// <remarks>
/// The <see cref="LocalizedWikiPage"/> under test is built by a real <see cref="WikiLocalizationService"/>
/// over an <see cref="InMemoryWikiService"/> rather than by hand. That service is the only thing allowed to
/// construct the record, and its own normalisation is what makes the record's locale invariants hold —
/// hand-building one here would let these tests pass against a state the production path cannot produce.
/// </remarks>
public class WikiPrerenderLocaleTests
{
	private const string Canonical = "https://x/wiki/main/general/dragons";

	private static (InMemoryWikiService Storage, WikiLocalizationService Localization) Build()
	{
		var storage = new InMemoryWikiService(new WikiMarkdigPipeline());
		var monitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		monitor.CurrentValue.Returns(TestSharpMushOptions.Create());
		var localization = new WikiLocalizationService(
			storage, new WikiLocaleResolver(monitor), NullLogger<WikiLocalizationService>.Instance);
		return (storage, localization);
	}

	/// <summary>Creates an English page, optionally with a published French translation, and resolves it.</summary>
	private static async Task<(LocalizedWikiPage Page, IReadOnlyList<string> Locales)> ResolveAsync(
		string? requestedLocale, bool withFrench)
	{
		var (storage, localization) = Build();
		var page = (await storage.CreateAsync(
			"Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).AsT0;

		if (withFrench)
		{
			await storage.UpsertTranslationAsync(
				page.Id, "fr", "Dragons (fr)", "corps fr", "#2", null, published: true,
				expectedRevisionNumber: null);
		}

		return (
			await localization.LocalizeAsync(page, requestedLocale, includeDrafts: false),
			await localization.GetVisibleLocalesAsync(page, includeDrafts: false));
	}

	[Test]
	public async Task HtmlLangAttribute_ReflectsTheServedLocale()
	{
		var (page, locales) = await ResolveAsync("fr", withFrench: true);

		var html = WikiController.GeneratePrerenderHtml(page, Canonical, locales, "en");

		await Assert.That(html).Contains("<html lang=\"fr\">");
		await Assert.That(html).DoesNotContain("<html lang=\"en\">");
	}

	[Test]
	public async Task HtmlLangAttribute_IsTheSourceLocaleWhenNoTranslationWasServed()
	{
		// The mirror of the case above: hardcoding "fr" would satisfy it just as well.
		var (page, locales) = await ResolveAsync("fr", withFrench: false);

		var html = WikiController.GeneratePrerenderHtml(page, Canonical, locales, "en");

		await Assert.That(html)
			.Contains("<html lang=\"en\">")
			.Because("a fallback served English, and telling a crawler otherwise is worse than saying nothing");
	}

	[Test]
	public async Task AlternateLinks_AreEmittedForEveryAvailableLocale()
	{
		var (page, locales) = await ResolveAsync(null, withFrench: true);

		var html = WikiController.GeneratePrerenderHtml(page, Canonical, locales, "en");

		await Assert.That(html).Contains("hreflang=\"en\"");
		await Assert.That(html).Contains("hreflang=\"fr\"");
		await Assert.That(html).Contains("?lang=fr");
	}

	[Test]
	public async Task XDefaultAlternate_PointsAtTheUnsuffixedCanonical()
	{
		var (page, locales) = await ResolveAsync(null, withFrench: true);

		var html = WikiController.GeneratePrerenderHtml(page, Canonical, locales, "en");

		await Assert.That(html)
			.Contains($"<link rel=\"alternate\" hreflang=\"x-default\" href=\"{Canonical}\" />")
			.Because("x-default is where a reader with no matching language lands");
	}

	[Test]
	public async Task Canonical_StaysAtTheUnsuffixedSlug()
	{
		var (page, locales) = await ResolveAsync("fr", withFrench: true);

		var html = WikiController.GeneratePrerenderHtml(page, Canonical, locales, "en");

		await Assert.That(html).Contains($"<link rel=\"canonical\" href=\"{Canonical}\" />");
		await Assert.That(html)
			.DoesNotContain($"rel=\"canonical\" href=\"{Canonical}?lang=")
			.Because("?lang= is a view of one canonical page, never a page of its own");
	}

	[Test]
	public async Task NoAlternateLinks_WhenOnlyOneLocaleExists()
	{
		var (page, locales) = await ResolveAsync(null, withFrench: false);

		var html = WikiController.GeneratePrerenderHtml(page, Canonical, locales, "en");

		await Assert.That(html)
			.DoesNotContain("rel=\"alternate\"")
			.Because("a single-locale page has nothing to alternate to");
	}

	[Test]
	public async Task JsonLd_DeclaresTheServedLanguageAndTheTranslatedTitle()
	{
		var (page, locales) = await ResolveAsync("fr", withFrench: true);

		var html = WikiController.GeneratePrerenderHtml(page, Canonical, locales, "en");

		await Assert.That(html).Contains("\"inLanguage\":\"fr\"");
		await Assert.That(html).Contains("Dragons (fr)");
	}

	[Test]
	public async Task Body_UsesTheResolvedTitleAndBodyNotThePages()
	{
		// Reading page.Page for content is the specific mistake LocalizedWikiPage exists to prevent, and it
		// would show up here as an English heading over French prose.
		var (page, locales) = await ResolveAsync("fr", withFrench: true);

		var html = WikiController.GeneratePrerenderHtml(page, Canonical, locales, "en");

		await Assert.That(html).Contains("<h1>Dragons (fr)</h1>");
		await Assert.That(html).Contains("corps fr");
		await Assert.That(html).DoesNotContain("en body");
	}

	[Test]
	public async Task CharacterPrerender_AlsoCarriesTheLocaleAndAlternates()
	{
		// The character generator is a separate method with its own <head>, which is exactly how one of the
		// two ends up still claiming English.
		var (page, locales) = await ResolveAsync("fr", withFrench: true);

		var html = WikiController.GenerateCharacterPrerenderHtml(page, Canonical, locales, "en");

		await Assert.That(html).Contains("<html lang=\"fr\">");
		await Assert.That(html).Contains("hreflang=\"x-default\"");
	}

	[Test]
	public async Task DraftTranslation_IsNeitherServedNorAdvertised()
	{
		var (storage, localization) = Build();
		var page = (await storage.CreateAsync(
			"Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.UpsertTranslationAsync(
			page.Id, "fr", "Brouillon", "corps brouillon", "#2", null, published: false,
			expectedRevisionNumber: null);

		var localized = await localization.LocalizeAsync(page, "fr", includeDrafts: false);
		var locales = await localization.GetVisibleLocalesAsync(page, includeDrafts: false);
		var html = WikiController.GeneratePrerenderHtml(localized, Canonical, locales, "en");

		await Assert.That(html).DoesNotContain("corps brouillon");
		await Assert.That(html)
			.DoesNotContain("hreflang=\"fr\"")
			.Because("advertising a locale a crawler cannot read is a lie that costs ranking");
	}
}
