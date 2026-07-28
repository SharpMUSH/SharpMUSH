using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// <see cref="LocalizedWikiPage.IsFallback"/> drives a user-visible banner, and it compares
/// <em>languages</em>, not tags. Serving <c>fr</c> to an <c>fr-CA</c> reader must not banner every
/// Canadian visit; serving <c>en</c> to that reader must.
/// </summary>
public class LocalizedWikiPageTests
{
	private static WikiPage UnstampedPage() => new(
		Id: "1", Slug: "dragons", Title: "Dragons", Namespace: "main",
		MarkdownSource: "en body", RenderedHtml: "<p>en body</p>", PlainText: "en body",
		AuthorDbref: "#1", LastEditorDbref: "#1",
		CreatedAt: DateTimeOffset.UnixEpoch, UpdatedAt: DateTimeOffset.UnixEpoch,
		IsProtected: false, RevisionNumber: 1);

	private static WikiPage BarePage() => UnstampedPage() with
	{
		Category = "general",
		SourceLocale = "en",
	};

	private static LocalizedWikiPage Localized(string served, string requested) => new(
		Page: BarePage(),
		Locale: served,
		RequestedLocale: requested,
		Title: "T", MarkdownSource: "m", RenderedHtml: "<p>m</p>", PlainText: "m",
		Published: true, RevisionNumber: 1);

	[Test]
	[Arguments("fr", "fr", false)]
	[Arguments("fr", "fr-CA", false)]
	[Arguments("fr-CA", "fr", false)]
	[Arguments("en", "en-GB", false)]
	[Arguments("en", "fr", true)]
	[Arguments("en", "fr-CA", true)]
	[Arguments("fr", "en", true)]
	public async Task IsFallback_ComparesLanguagesNotTags(string served, string requested, bool expected)
	{
		await Assert.That(Localized(served, requested).IsFallback).IsEqualTo(expected);
	}

	[Test]
	public async Task ResolvedContentLivesOnTheWrapperNotThePage()
	{
		var localized = Localized("fr", "fr") with { Title = "Dragons (fr)", MarkdownSource = "corps fr" };

		await Assert.That(localized.Title).IsEqualTo("Dragons (fr)");
		await Assert.That(localized.Page.Title)
			.IsEqualTo("Dragons")
			.Because("the source page must keep its own title so nobody renders a mixed-language page");
	}

	[Test]
	public async Task WikiPage_SourceLocaleInitializerDefaultsToEmptyNotEnglish()
	{
		// Constructed without SourceLocale, so this is the initializer default the name claims to test.
		// Asserting on `BarePage() with { SourceLocale = string.Empty }` would only prove assignment works.
		var page = UnstampedPage();

		await Assert.That(page.SourceLocale)
			.IsEqualTo(string.Empty)
			.Because("a property initializer cannot read configuration, and hardcoding 'en' would mislabel "
				+ "every page on a non-English game. Empty is a transient pre-backfill state, NOT a "
				+ "read-time synonym for Wiki.DefaultLocale — see Task 10");
	}

	[Test]
	public async Task WikiRevision_LocaleDefaultsToEmptyMeaningTheSourceStream()
	{
		var revision = new WikiRevision(
			Id: "1:1", PageId: "1", RevisionNumber: 1, MarkdownSource: "v1",
			EditorDbref: "#1", Timestamp: DateTimeOffset.UnixEpoch, EditSummary: null);

		await Assert.That(revision.Locale).IsEqualTo(string.Empty);
	}
}
