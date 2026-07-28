using Microsoft.Extensions.DependencyInjection;
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

		await Assert.That(result.IsT2)
			.IsTrue()
			.Because("shadowing the source locale is a malformed request, not a race the caller lost");
	}

	[Test]
	public async Task UpsertTranslationAsync_RejectsAnUnknownPage()
	{
		var ghost = $"node_wiki_pages/ghost_{Guid.NewGuid():N}";

		var result = await Wiki.UpsertTranslationAsync(ghost, "fr", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		await Assert.That(result.IsT2).IsTrue();
	}

	[Test]
	public async Task GetTranslationAsync_ReturnsNotFoundForAMissingLocale()
	{
		var page = await CreateSourcePageAsync("MissingLocale");

		await Assert.That((await Wiki.GetTranslationAsync(page.Id, "de")).IsT1).IsTrue();
	}

	/// <summary>
	/// Pages the whole translation stream and returns every row belonging to <paramref name="pageId"/>.
	/// The session database is shared, so nothing here may assume a total count — but paging really does
	/// have to be exercised, because a provider whose LIMIT/START (or SKIP/LIMIT) is transposed returns a
	/// plausible-looking answer that a single unpaged fetch would never catch.
	/// </summary>
	private async Task<List<WikiTranslation>> ScanTranslationsAsync(string pageId)
	{
		var found = new List<WikiTranslation>();
		const int window = 5;
		for (var skip = 0; ; skip += window)
		{
			var batch = await Wiki.GetAllTranslationsAsync(skip, window);
			if (batch.Count == 0) break;
			found.AddRange(batch.Where(t => t.PageId == pageId));
			if (batch.Count < window) break;
		}

		return found;
	}

	[Test]
	public async Task GetAllTranslationsAsync_PagesTheWholeStreamWithBodies()
	{
		var page = await CreateSourcePageAsync("BulkScan");
		await Wiki.UpsertTranslationAsync(
			page.Id, "fr", "Titre fr", "corps bulk fr", "#2", null, true, expectedRevisionNumber: null);
		await Wiki.UpsertTranslationAsync(
			page.Id, "de", "Titel de", "korpus bulk de", "#2", null, true, expectedRevisionNumber: null);

		var mine = await ScanTranslationsAsync(page.Id);

		await Assert.That(mine.Select(t => t.Locale).Order()).IsEquivalentTo(new[] { "de", "fr" });
		await Assert.That(mine.Single(t => t.Locale == "fr").MarkdownSource).IsEqualTo("corps bulk fr");
		await Assert.That(mine.Single(t => t.Locale == "de").PlainText)
			.Contains("korpus")
			.Because("in-game search matches PlainText, so a provider returning bodyless rows here would "
				+ "silently make every translation unsearchable rather than fail");
	}

	[Test]
	public async Task GetAllTranslationsAsync_IncludesUnpublishedDrafts()
	{
		var page = await CreateSourcePageAsync("BulkDraft");
		await Wiki.UpsertTranslationAsync(
			page.Id, "de", "Entwurf", "korpus entwurf", "#2", null, published: false, expectedRevisionNumber: null);

		var mine = await ScanTranslationsAsync(page.Id);

		await Assert.That(mine.Single().Published)
			.IsFalse()
			.Because("this mirrors GetAllPagesAsync: storage returns every row and the caller filters");
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
	public async Task GetRevisionAsync_NeverReturnsATranslationRevision()
	{
		// The two rollback paths (WikiController.Rollback, @wiki/rollback) feed this method's Markdown
		// straight back into the source page, so a translation revision leaking through here would restore
		// French prose over an English page. Translation streams restart at 1, so r1 is the collision, and
		// which of the two rows a provider happens to return is not something to leave to query ordering.
		var page = await CreateSourcePageAsync("RevByNumber");
		await Wiki.UpsertTranslationAsync(page.Id, "fr", "T", "corps fr", "#2", null, true, expectedRevisionNumber: null);

		var revision = await Wiki.GetRevisionAsync(page.Id, 1);

		await Assert.That(revision.IsT0).IsTrue();
		await Assert.That(revision.AsT0.Locale).IsEqualTo(string.Empty);
		await Assert.That(revision.AsT0.MarkdownSource)
			.IsEqualTo("en **body**")
			.Because("a rollback must restore the source body, never a translation's");
	}

	[Test]
	public async Task GetRevisionForLocaleAsync_ReturnsTheRequestedLocalesRevisionNotTheSources()
	{
		// Revision 1 exists in both streams with different prose, so a provider that ignores the locale
		// filter returns the English row and the assertion below is the only thing that notices. This is
		// what makes GET /revisions/{n}?lang=fr serve French rather than diffing French against English.
		var page = await CreateSourcePageAsync("RevByLocale");
		await Wiki.UpsertTranslationAsync(page.Id, "fr", "T", "corps fr", "#2", null, true, expectedRevisionNumber: null);

		var french = await Wiki.GetRevisionForLocaleAsync(page.Id, "fr", 1);
		var source = await Wiki.GetRevisionForLocaleAsync(page.Id, string.Empty, 1);

		await Assert.That(french.IsT0).IsTrue();
		await Assert.That(french.AsT0.Locale).IsEqualTo("fr");
		await Assert.That(french.AsT0.MarkdownSource)
			.IsEqualTo("corps fr")
			.Because("the French revision 1 is not the English revision 1");
		await Assert.That(source.IsT0).IsTrue();
		await Assert.That(source.AsT0.Locale).IsEqualTo(string.Empty);
		await Assert.That(source.AsT0.MarkdownSource)
			.IsEqualTo("en **body**")
			.Because("the empty stream stays the source's, on every backend");
	}

	[Test]
	public async Task GetRevisionForLocaleAsync_IsNotFoundWhenThatStreamLacksTheNumber()
	{
		// The failure mode this rules out is a provider whose locale predicate silently matches nothing —
		// or everything. Source revision 2 exists; asking the French stream for 2 must be NotFound, not
		// the English revision 2.
		var page = await CreateSourcePageAsync("RevByLocaleMissing");
		await Wiki.UpdateAsync(page.Id, "en v2", "#1");
		await Wiki.UpsertTranslationAsync(page.Id, "fr", "T", "corps fr", "#2", null, true, expectedRevisionNumber: null);

		var missing = await Wiki.GetRevisionForLocaleAsync(page.Id, "fr", 2);
		var present = await Wiki.GetRevisionForLocaleAsync(page.Id, "fr", 1);

		await Assert.That(missing.IsT1).IsTrue();
		await Assert.That(present.IsT0)
			.IsTrue()
			.Because("a provider matching nothing at all would also make the NotFound above pass");
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
		await Assert.That(loser.AsT1)
			.IsEqualTo(WikiWriteConflict.StaleRevision)
			.Because("a second revision 2 for (PageId, Locale) must be refused, never silently accepted");

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

		await Assert.That(again.AsT1).IsEqualTo(WikiWriteConflict.AlreadyExists);
		await Assert.That((await Wiki.GetTranslationAsync(page.Id, "fr")).AsT0.MarkdownSource)
			.IsEqualTo("corps v1");
	}

	[Test]
	public async Task UpsertTranslationAsync_ReportsAConflictWhenTheTranslationWasDeletedMidEdit()
	{
		// The third lost-write shape, and the one all four implementations used to phrase differently — one
		// of them (Memgraph) folded it into the stale-revision wording, so the HTTP boundary's phrase match
		// answered 409 there and 400 everywhere else for the same race. Pinned per provider now.
		var page = await CreateSourcePageAsync("DeletedMidEdit");
		await Wiki.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);
		await Wiki.DeleteTranslationAsync(page.Id, "fr", "#3");

		var orphaned = await Wiki.UpsertTranslationAsync(
			page.Id, "fr", "orphelin", "corps orphelin", "#2", null, true, expectedRevisionNumber: 1);

		await Assert.That(orphaned.AsT1).IsEqualTo(WikiWriteConflict.TranslationGone);
		await Assert.That((await Wiki.GetTranslationAsync(page.Id, "fr")).IsT1)
			.IsTrue()
			.Because("a compare-and-swap must not resurrect a row somebody deliberately deleted");
	}

	[Test]
	public async Task ConcurrentUpsertsWithTheSameExpectedRevisionLoseNoProse()
	{
		// The spec's concurrency case. Needs a real backend: the in-memory dictionary cannot reproduce the
		// race. Whichever ordering the store picks, exactly one writer wins, the other gets a
		// WikiWriteConflict, and the loser's markdown appears in no revision.
		//
		// Repeated deliberately. A single attempt usually resolves through the tidy "zero rows matched"
		// branch and never reaches the one where the store aborts the loser mid-write — on ArangoDB that
		// second branch reported a plain error, so a genuine lost write answered 400 instead of 409, and one
		// attempt found it perhaps one run in five. Both branches must classify the loser identically.
		for (var attempt = 0; attempt < 10; attempt++)
		{
			var page = await CreateSourcePageAsync($"Concurrent{attempt}");
			await Wiki.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);

			var results = await Task.WhenAll(
				Wiki.UpsertTranslationAsync(page.Id, "fr", "A", "corps a", "#2", null, true, expectedRevisionNumber: 1),
				Wiki.UpsertTranslationAsync(page.Id, "fr", "B", "corps b", "#3", null, true, expectedRevisionNumber: 1));

			await Assert.That(results.Count(r => r.IsT0))
				.IsEqualTo(1)
				.Because("exactly one compare-and-swap on the same expected revision may succeed");
			await Assert.That(results.Single(r => !r.IsT0).AsT1)
				.IsEqualTo(WikiWriteConflict.StaleRevision)
				.Because("the loser lost a race, and 400 would tell it to edit a body that was never wrong");

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
}
