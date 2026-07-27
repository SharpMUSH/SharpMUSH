using Microsoft.Extensions.DependencyInjection;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Wiki;

/// <summary>
/// The translation overlay's CRUD and index semantics against the configured DB backend. The backend is
/// selected by <c>SHARPMUSH_DATABASE_PROVIDER</c> (arangodb / memgraph / surrealdb) and CI runs this
/// assembly once per provider, so this one class is all three providers' contract.
///
/// Written deliberately before the three hand-written backend implementations: the five CRUD methods are
/// mechanical, but the existing revision indexes differ per store — unique on SurrealDB, non-unique on
/// ArangoDB, absent on Memgraph — and this file is what catches that.
///
/// The <b>negative</b> cases at the bottom carry the weight. A suite that only writes valid data cannot
/// distinguish a real unique constraint from a missing one, which is exactly how these three drifted
/// apart, so "rejects a duplicate (PageId, Locale, RevisionNumber)" and "accepts a translation revision 1
/// beside a source revision 1" are asserted explicitly.
///
/// The session database is shared and never reset, so every page title is uniquified.
/// </summary>
[NotInParallel]
public class WikiTranslationIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	private IWikiService Wiki => WebAppFactory.Services.GetRequiredService<ISharpDatabase>() as IWikiService
		?? throw new InvalidOperationException("ISharpDatabase does not implement IWikiService in this configuration.");

	/// <summary>Creates a uniquely-named English source page and returns it.</summary>
	private async Task<WikiPage> CreateSourcePageAsync(string label, string sourceLocale = "en")
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var result = await Wiki.CreateAsync(
			$"{label} {uid}", "en **body**", "#1", WikiNamespace.Main, "general", sourceLocale);
		await Assert.That(result.IsT0).IsTrue();
		return result.AsT0;
	}

	[Test]
	public async Task CreateAsync_PersistsSourceLocale()
	{
		var page = await CreateSourcePageAsync("SrcLocale", sourceLocale: "fr-CA");

		var reread = await Wiki.GetBySlugAsync(page.Slug, page.Category, WikiNamespace.Main);

		await Assert.That(reread.IsT0).IsTrue();
		await Assert.That(reread.AsT0.SourceLocale)
			.IsEqualTo("fr-CA")
			.Because("SourceLocale must round-trip through the provider's serializer");
	}

	[Test]
	public async Task UpsertTranslationAsync_RoundTripsThroughTheProvider()
	{
		var page = await CreateSourcePageAsync("Upsert");

		var created = await Wiki.UpsertTranslationAsync(
			page.Id, "fr", "Titre fr", "corps **fr**", "#2", "première",
			published: true, expectedRevisionNumber: null);

		await Assert.That(created.IsT0).IsTrue();
		var fetched = await Wiki.GetTranslationAsync(page.Id, "fr");
		await Assert.That(fetched.IsT0).IsTrue();
		await Assert.That(fetched.AsT0.Title).IsEqualTo("Titre fr");
		await Assert.That(fetched.AsT0.MarkdownSource).IsEqualTo("corps **fr**");
		await Assert.That(fetched.AsT0.RenderedHtml).Contains("<strong>fr</strong>");
		await Assert.That(fetched.AsT0.RevisionNumber).IsEqualTo(1);
		await Assert.That(fetched.AsT0.Published).IsTrue();
	}

	[Test]
	public async Task UpsertTranslationAsync_IsAnUpsertNotAnInsert()
	{
		// The (PageId, Locale) unique index is what this asserts. A provider whose index is missing or
		// non-unique will produce two rows here and fail on the count, which is the whole point of
		// running this file against every store.
		var page = await CreateSourcePageAsync("UpsertTwice");
		await Wiki.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);

		var second = await Wiki.UpsertTranslationAsync(
			page.Id, "fr", "v2", "corps v2", "#3", "révision", true, expectedRevisionNumber: 1);

		await Assert.That(second.IsT0).IsTrue();
		await Assert.That(second.AsT0.RevisionNumber).IsEqualTo(2);
		await Assert.That(second.AsT0.MarkdownSource).IsEqualTo("corps v2");
		var summaries = await Wiki.GetTranslationsAsync(page.Id);
		await Assert.That(summaries.Count).IsEqualTo(1);
	}

	[Test]
	public async Task UpsertTranslationAsync_TwoLocalesOnOnePageAreDistinctRows()
	{
		var page = await CreateSourcePageAsync("TwoLocales");

		await Wiki.UpsertTranslationAsync(page.Id, "fr", "Titre fr", "fr", "#2", null, true, expectedRevisionNumber: null);
		await Wiki.UpsertTranslationAsync(page.Id, "de", "Titel de", "de", "#2", null, true, expectedRevisionNumber: null);

		var summaries = await Wiki.GetTranslationsAsync(page.Id);

		await Assert.That(summaries.Count).IsEqualTo(2);
		await Assert.That(summaries.Select(s => s.Locale).Order()).IsEquivalentTo(new[] { "de", "fr" });
	}

	[Test]
	public async Task UpsertTranslationAsync_SameLocaleOnTwoPagesAreDistinctRows()
	{
		var first = await CreateSourcePageAsync("SameLocaleA");
		var second = await CreateSourcePageAsync("SameLocaleB");

		await Wiki.UpsertTranslationAsync(first.Id, "fr", "A fr", "a", "#2", null, true, expectedRevisionNumber: null);
		await Wiki.UpsertTranslationAsync(second.Id, "fr", "B fr", "b", "#2", null, true, expectedRevisionNumber: null);

		await Assert.That((await Wiki.GetTranslationAsync(first.Id, "fr")).AsT0.Title).IsEqualTo("A fr");
		await Assert.That((await Wiki.GetTranslationAsync(second.Id, "fr")).AsT0.Title)
			.IsEqualTo("B fr")
			.Because("the unique index is on (PageId, Locale), not on Locale alone");
	}

	[Test]
	public async Task UpsertTranslationAsync_NormalisesTheLocaleAndFindsItByEitherCase()
	{
		var page = await CreateSourcePageAsync("LocaleCase");

		var created = await Wiki.UpsertTranslationAsync(page.Id, "FR-ca", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		await Assert.That(created.AsT0.Locale).IsEqualTo("fr-CA");
		await Assert.That((await Wiki.GetTranslationAsync(page.Id, "fr-ca")).IsT0).IsTrue();
		await Assert.That((await Wiki.GetTranslationAsync(page.Id, "FR-CA")).IsT0).IsTrue();
	}

	[Test]
	public async Task UpsertTranslationAsync_RejectsShadowingTheSourceLocale()
	{
		var page = await CreateSourcePageAsync("Shadow", sourceLocale: "en");

		var result = await Wiki.UpsertTranslationAsync(page.Id, "en", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1).IsTypeOf<Error<string>>();
	}

	[Test]
	public async Task UpsertTranslationAsync_RejectsAnUnknownPage()
	{
		var ghost = $"node_wiki_pages/ghost_{Guid.NewGuid():N}";

		var result = await Wiki.UpsertTranslationAsync(ghost, "fr", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		await Assert.That(result.IsT1).IsTrue();
	}

	[Test]
	public async Task GetTranslationAsync_ReturnsNotFoundForAMissingLocale()
	{
		var page = await CreateSourcePageAsync("MissingLocale");

		await Assert.That((await Wiki.GetTranslationAsync(page.Id, "de")).IsT1).IsTrue();
	}

	[Test]
	public async Task GetTranslationsAsync_IncludesUnpublishedDrafts()
	{
		var page = await CreateSourcePageAsync("DraftListing");
		await Wiki.UpsertTranslationAsync(page.Id, "de", "Entwurf", "m", "#2", null, published: false, expectedRevisionNumber: null);

		var summaries = await Wiki.GetTranslationsAsync(page.Id);

		await Assert.That(summaries.Single().Published)
			.IsFalse()
			.Because("storage returns every row; visibility filtering happens above the DB layer");
	}

	[Test]
	public async Task GetRevisionsForLocaleAsync_IsASeparateStreamFromTheSource()
	{
		var page = await CreateSourcePageAsync("RevStreams");
		await Wiki.UpdateAsync(page.Id, "en v2", "#1");
		await Wiki.UpsertTranslationAsync(page.Id, "fr", "T", "fr1", "#2", null, true, expectedRevisionNumber: null);
		await Wiki.UpsertTranslationAsync(page.Id, "fr", "T", "fr2", "#2", null, true, expectedRevisionNumber: 1);

		var french = await Wiki.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20);
		var source = await Wiki.GetRevisionsAsync(page.Id);

		await Assert.That(french.Count).IsEqualTo(2);
		await Assert.That(french.All(r => r.Locale == "fr")).IsTrue();
		await Assert.That(french.Select(r => r.MarkdownSource)).Contains("fr2");
		await Assert.That(source.Count).IsEqualTo(2);
		await Assert.That(source.All(r => r.Locale.Length == 0))
			.IsTrue()
			.Because("GetRevisionsAsync must stay the source-locale stream for its existing callers");
	}

	[Test]
	public async Task DeleteTranslationAsync_RemovesOnlyThatLocale()
	{
		var page = await CreateSourcePageAsync("DeleteOne");
		await Wiki.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, true, expectedRevisionNumber: null);
		await Wiki.UpsertTranslationAsync(page.Id, "de", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		var deleted = await Wiki.DeleteTranslationAsync(page.Id, "fr", "#2");

		await Assert.That(deleted.IsT0).IsTrue();
		await Assert.That((await Wiki.GetTranslationAsync(page.Id, "fr")).IsT1).IsTrue();
		await Assert.That((await Wiki.GetTranslationAsync(page.Id, "de")).IsT0).IsTrue();
		await Assert.That((await Wiki.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20)).Count).IsEqualTo(0);
		await Assert.That((await Wiki.GetRevisionsForLocaleAsync(page.Id, "de", 0, 20)).Count).IsEqualTo(1);
	}

	[Test]
	public async Task DeleteTranslationAsync_ReturnsNotFoundForAMissingLocale()
	{
		var page = await CreateSourcePageAsync("DeleteMissing");

		await Assert.That((await Wiki.DeleteTranslationAsync(page.Id, "fr", "#2")).IsT1).IsTrue();
	}

	[Test]
	public async Task DeleteAsync_CascadesToTranslationsAndTheirRevisions()
	{
		var page = await CreateSourcePageAsync("Cascade");
		await Wiki.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, true, expectedRevisionNumber: null);
		await Wiki.UpsertTranslationAsync(page.Id, "de", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		await Wiki.DeleteAsync(page.Id, "#1");

		await Assert.That((await Wiki.GetTranslationsAsync(page.Id)).Count).IsEqualTo(0);
		await Assert.That((await Wiki.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20)).Count).IsEqualTo(0);
		await Assert.That((await Wiki.GetRevisionsForLocaleAsync(page.Id, "de", 0, 20)).Count).IsEqualTo(0);
		await Assert.That((await Wiki.GetRevisionsAsync(page.Id)).Count).IsEqualTo(0);
	}

	// ---- Negative cases: the revision constraint itself ---------------------
	//
	// Everything above would pass on a store with no revision constraint at all. These four will not.

	[Test]
	public async Task RevisionIndex_AcceptsATranslationRevisionOneBesideASourceRevisionOne()
	{
		// (PageId, RevisionNumber) is NOT unique any more: a translation's stream restarts at 1 while the
		// source page already has a revision 1. SurrealDB's pre-existing wiki_revision_page_rev UNIQUE
		// index rejects this outright, which is the whole reason Task 9 must redefine it.
		var page = await CreateSourcePageAsync("RevOneTwice");

		var created = await Wiki.UpsertTranslationAsync(
			page.Id, "fr", "Titre fr", "corps fr", "#2", null, true, expectedRevisionNumber: null);

		await Assert.That(created.IsT0)
			.IsTrue()
			.Because("a translation revision 1 must coexist with the source's revision 1");
		var french = await Wiki.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20);
		var source = await Wiki.GetRevisionsAsync(page.Id);
		await Assert.That(french.Single().RevisionNumber).IsEqualTo(1);
		await Assert.That(source.Single().RevisionNumber).IsEqualTo(1);
		await Assert.That(source.Single().Locale)
			.IsEqualTo(string.Empty)
			.Because("the two rows are distinguished by Locale, which is why it is in the constraint");
	}

	[Test]
	public async Task RevisionIndex_RejectsADuplicatePageLocaleRevisionNumber()
	{
		// Two writers both loaded revision 1 and both compute revision 2. Exactly one may land. A store
		// with no constraint and a read-then-write upsert writes both and fails the count below, which is
		// the assertion that tells a real constraint from a missing one.
		var page = await CreateSourcePageAsync("DupRevision");
		await Wiki.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);

		var winner = await Wiki.UpsertTranslationAsync(
			page.Id, "fr", "v2", "corps v2", "#3", null, true, expectedRevisionNumber: 1);
		var loser = await Wiki.UpsertTranslationAsync(
			page.Id, "fr", "perdu", "corps perdu", "#4", null, true, expectedRevisionNumber: 1);

		await Assert.That(winner.IsT0).IsTrue();
		await Assert.That(loser.IsT1)
			.IsTrue()
			.Because("a second revision 2 for (PageId, Locale) must be refused, never silently accepted");
		await Assert.That(loser.AsT1).IsTypeOf<Error<string>>();

		var revisions = await Wiki.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20);
		await Assert.That(revisions.Count(r => r.RevisionNumber == 2))
			.IsEqualTo(1)
			.Because("two rows numbered 2 is the exact corruption the unique constraint exists to stop");
		await Assert.That(revisions.Select(r => r.MarkdownSource))
			.DoesNotContain("corps perdu")
			.Because("a rejected write must leave no revision behind");
		await Assert.That((await Wiki.GetTranslationAsync(page.Id, "fr")).AsT0.MarkdownSource)
			.IsEqualTo("corps v2");
	}

	[Test]
	public async Task UpsertTranslationAsync_CreateOnlyRefusesAnExistingTranslation()
	{
		var page = await CreateSourcePageAsync("CreateOnly");
		await Wiki.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);

		var again = await Wiki.UpsertTranslationAsync(
			page.Id, "fr", "écrasé", "corps écrasé", "#3", null, true, expectedRevisionNumber: null);

		await Assert.That(again.IsT1).IsTrue();
		await Assert.That((await Wiki.GetTranslationAsync(page.Id, "fr")).AsT0.MarkdownSource)
			.IsEqualTo("corps v1");
	}

	[Test]
	public async Task ConcurrentUpsertsWithTheSameExpectedRevisionLoseNoProse()
	{
		// The spec's concurrency case. Needs a real backend: the in-memory dictionary cannot reproduce the
		// race. Whichever ordering the store picks, exactly one writer wins, the other gets Error<string>,
		// and the loser's markdown appears in no revision.
		var page = await CreateSourcePageAsync("Concurrent");
		await Wiki.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);

		var results = await Task.WhenAll(
			Wiki.UpsertTranslationAsync(page.Id, "fr", "A", "corps a", "#2", null, true, expectedRevisionNumber: 1),
			Wiki.UpsertTranslationAsync(page.Id, "fr", "B", "corps b", "#3", null, true, expectedRevisionNumber: 1));

		await Assert.That(results.Count(r => r.IsT0))
			.IsEqualTo(1)
			.Because("exactly one compare-and-swap on the same expected revision may succeed");
		await Assert.That(results.Count(r => r.IsT1)).IsEqualTo(1);

		var revisions = await Wiki.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20);
		await Assert.That(revisions.Count).IsEqualTo(2);
		var winnerMarkdown = results.Single(r => r.IsT0).AsT0.MarkdownSource;
		var loserMarkdown = winnerMarkdown == "corps a" ? "corps b" : "corps a";
		await Assert.That(revisions.Select(r => r.MarkdownSource))
			.DoesNotContain(loserMarkdown)
			.Because("the loser is never retried, so its prose must not reach the store at all");
		await Assert.That((await Wiki.GetTranslationsAsync(page.Id)).Count).IsEqualTo(1);
	}
}
