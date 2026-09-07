using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// The Lightning wiki area (Task 17) directly against a <see cref="LightningDatabase"/> instance — no
/// host, no DI — mirroring <see cref="MigrationTests"/>'s fixture. <c>WikiServiceIntegrationTests</c> and
/// <c>WikiTranslationIntegrationTests</c> already run this interface's whole contract against whichever
/// provider is configured; what this class adds is assertions against the <em>storage shape</em> — the
/// slug index, the per-locale revision ranges and the delete cascade — which no interface-level test can
/// see, and which is exactly where an LMDB key-layout mistake hides.
/// </summary>
public class WikiTests
{
	// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private static async Task WithDatabaseAsync(Func<LightningDatabase, IWikiService, Task> body)
	{
		var path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		var db = Create(path);
		try
		{
			await body(db, db);
		}
		finally
		{
			db.Store.Dispose();
			if (Directory.Exists(path))
			{
				try
				{
					Directory.Delete(path, recursive: true);
				}
				catch (IOException)
				{
					// Best-effort: a lingering LMDB lock file (mdb.lck) can outlive the writer thread's
					// join by a few milliseconds under load — matches MigrationTests's own cleanup.
				}
			}
		}
	}

	/// <summary>The slug-index key the provider writes, rebuilt from the same shared helper it uses.</summary>
	private static byte[] SlugKey(string ns, string? category, string slug)
		=> Keys.Lower(WikiHelpers.SlugKey(ns, category, slug));

	/// <summary>The key prefix under which every child row of one page lives.</summary>
	private static byte[] PagePrefix(string pageId) => Keys.Concat(Keys.Str(pageId), Keys.Sep);

	[Test]
	public async Task PageIsReachableByIdAndByItsSlugIndexEntry() => await WithDatabaseAsync(async (db, wiki) =>
	{
		var created = await wiki.CreateAsync("Dragon Lore", "Hello **world**.", "#1", WikiNamespace.Main, "Lore", "en");

		await Assert.That(created.IsT0).IsTrue();
		var page = created.AsT0;
		await Assert.That(page.Slug).IsEqualTo("dragon_lore");
		await Assert.That(page.Category).IsEqualTo("lore");
		await Assert.That(page.SourceLocale).IsEqualTo("en");
		await Assert.That(page.RevisionNumber).IsEqualTo(1);

		// Identity is (namespace, category, slug), and the lookup normalises a display title into it.
		await Assert.That((await wiki.GetBySlugAsync("Dragon Lore", "lore", WikiNamespace.Main)).AsT0.Id).IsEqualTo(page.Id);
		await Assert.That((await wiki.GetByIdAsync(page.Id)).AsT0.Title).IsEqualTo("Dragon Lore");
		await Assert.That((await wiki.GetBySlugAsync("dragon_lore", "general", WikiNamespace.Main)).IsT1).IsTrue();
		await Assert.That((await wiki.GetBySlugAsync("dragon_lore", "lore", WikiNamespace.Help)).IsT1).IsTrue();

		// A second page on the same identity is refused by the index, not by a scan.
		var duplicate = await wiki.CreateAsync("Dragon Lore", "again", "#1", WikiNamespace.Main, "lore");
		await Assert.That(duplicate.IsT1).IsTrue();

		var indexed = db.Store.Read(tx => tx.TryGet(Tables.WikiSlug, SlugKey("main", "lore", "dragon_lore"), out var v)
			? Keys.ReadDbref(v)
			: -1);
		await Assert.That(indexed).IsEqualTo(0L).Because("page keys come from the next_wiki counter, which starts at 0");
		await Assert.That(db.Store.Count(Tables.WikiPage)).IsEqualTo(1L);

		// An id from another provider's URL space must miss rather than throw.
		await Assert.That((await wiki.GetByIdAsync("node_wiki_pages/ghost")).IsT1).IsTrue();
	});

	[Test]
	public async Task UpdateAppendsToTheSourceStreamInRevisionOrder() => await WithDatabaseAsync(async (db, wiki) =>
	{
		var page = (await wiki.CreateAsync("Rev Stream", "v1", "#1")).AsT0;
		await wiki.UpdateAsync(page.Id, "v2", "#2", "second");
		var third = await wiki.UpdateAsync(page.Id, "v3", "#3", "third");

		await Assert.That(third.AsT0.RevisionNumber).IsEqualTo(3);
		await Assert.That(third.AsT0.LastEditorDbref).IsEqualTo("#3");

		var revisions = await wiki.GetRevisionsAsync(page.Id);
		await Assert.That(revisions.Select(r => r.RevisionNumber)).IsEquivalentTo(new[] { 3, 2, 1 });
		await Assert.That(revisions.All(r => r.Locale.Length == 0)).IsTrue();
		await Assert.That((await wiki.GetRevisionAsync(page.Id, 1)).AsT0.MarkdownSource).IsEqualTo("v1");
		await Assert.That((await wiki.GetRevisionAsync(page.Id, 4)).IsT1).IsTrue();

		// The stream is a contiguous ascending key range, so the last entry is the latest revision.
		var stored = db.Store.Read(tx => tx.Range(Tables.WikiRev, PagePrefix(page.Id)).Select(e => e.Key).ToList());
		await Assert.That(stored.Count).IsEqualTo(3);
		await Assert.That(stored[^1].AsSpan()[^4..].ToArray()).IsEquivalentTo(new byte[] { 0, 0, 0, 3 });
	});

	[Test]
	public async Task TranslationRoundTripsAndAStaleExpectedRevisionIsRefused() => await WithDatabaseAsync(async (db, wiki) =>
	{
		var page = (await wiki.CreateAsync("Trans Round", "en body", "#1", WikiNamespace.Main, "general", "en")).AsT0;

		var created = await wiki.UpsertTranslationAsync(
			page.Id, "FR-ca", "Titre", "corps v1", "#2", "première", published: true, expectedRevisionNumber: null);
		await Assert.That(created.IsT0).IsTrue();
		await Assert.That(created.AsT0.Locale).IsEqualTo("fr-CA");
		await Assert.That(created.AsT0.RevisionNumber).IsEqualTo(1);
		await Assert.That(created.AsT0.RenderedHtml).Contains("corps v1");

		var fetched = await wiki.GetTranslationAsync(page.Id, "fr-ca");
		await Assert.That(fetched.AsT0.MarkdownSource).IsEqualTo("corps v1");

		var second = await wiki.UpsertTranslationAsync(
			page.Id, "fr-CA", "Titre", "corps v2", "#3", null, published: true, expectedRevisionNumber: 1);
		await Assert.That(second.AsT0.RevisionNumber).IsEqualTo(2);
		await Assert.That(second.AsT0.CreatedAt).IsEqualTo(created.AsT0.CreatedAt)
			.Because("an update keeps the row's original creation stamp");

		// The compare-and-swap: the loser's expected revision no longer matches, and its prose must not
		// reach the store at all — no revision row, no overwrite of the winner.
		var stale = await wiki.UpsertTranslationAsync(
			page.Id, "fr-CA", "Perdu", "corps perdu", "#4", null, published: true, expectedRevisionNumber: 1);
		await Assert.That(stale.IsT1).IsTrue();
		await Assert.That(stale.AsT1).IsEqualTo(WikiWriteConflict.StaleRevision);

		var again = await wiki.UpsertTranslationAsync(
			page.Id, "fr-CA", "Écrasé", "corps écrasé", "#4", null, published: true, expectedRevisionNumber: null);
		await Assert.That(again.AsT1).IsEqualTo(WikiWriteConflict.AlreadyExists);

		await wiki.DeleteTranslationAsync(page.Id, "de", "#4");
		var gone = await wiki.UpsertTranslationAsync(
			page.Id, "de", "Weg", "korpus", "#4", null, published: true, expectedRevisionNumber: 1);
		await Assert.That(gone.AsT1).IsEqualTo(WikiWriteConflict.TranslationGone);

		var shadow = await wiki.UpsertTranslationAsync(
			page.Id, "en", "Shadow", "body", "#4", null, published: true, expectedRevisionNumber: null);
		await Assert.That(shadow.IsT2).IsTrue().Because("shadowing the source locale is a bad request, not a lost race");

		var french = await wiki.GetRevisionsForLocaleAsync(page.Id, "fr-CA", 0, 20);
		await Assert.That(french.Select(r => r.MarkdownSource)).IsEquivalentTo(new[] { "corps v2", "corps v1" });
		await Assert.That((await wiki.GetTranslationAsync(page.Id, "fr-CA")).AsT0.MarkdownSource).IsEqualTo("corps v2");
		await Assert.That((await wiki.GetRevisionsAsync(page.Id)).Single().MarkdownSource)
			.IsEqualTo("en body")
			.Because("the source stream is keyed by the empty locale, so a translation cannot land in it");
		await Assert.That(db.Store.Count(Tables.WikiTr)).IsEqualTo(1L);
	});

	/// <summary>
	/// Storage returns drafts to every reader — <c>IWikiService</c> puts visibility filtering above the DB
	/// layer (<c>IWikiLocalizationService</c> and the controllers decide who sees an unpublished page or
	/// translation, including the author-sees-own-draft rule). The one member that filters is
	/// <c>CountPagesAsync</c>, and only because a count rendered beside a filtered listing would otherwise
	/// be a disclosure channel. This pins both halves: a provider that quietly hid drafts would break the
	/// editor, and one whose count ignored the flag would leak how many drafts a namespace holds.
	/// </summary>
	[Test]
	public async Task DraftsAreReturnedToEveryReaderAndOnlyTheCountFiltersThem() => await WithDatabaseAsync(async (_, wiki) =>
	{
		var published = (await wiki.CreateAsync("Kept", "body", "#1", WikiNamespace.System)).AsT0;
		var draft = (await wiki.CreateAsync("Hidden", "body", "#1", WikiNamespace.System)).AsT0;

		// Recategorising also moves the slug-index entry, so the new identity resolves and the old one stops.
		var unpublished = await wiki.SetMetadataAsync(draft.Id, "Lore", ["Lore", "lore", " "], published: false);
		await Assert.That(unpublished.AsT0.Published).IsFalse();
		await Assert.That(unpublished.AsT0.Category).IsEqualTo("lore");
		await Assert.That(unpublished.AsT0.Tags).IsEquivalentTo(new[] { "lore" });
		await Assert.That((await wiki.GetBySlugAsync("hidden", "lore", WikiNamespace.System)).AsT0.Id).IsEqualTo(draft.Id);
		await Assert.That((await wiki.GetBySlugAsync("hidden", "general", WikiNamespace.System)).IsT1).IsTrue();

		await Assert.That(await wiki.CountPagesAsync(WikiNamespace.System, includeDrafts: true)).IsEqualTo(2);
		await Assert.That(await wiki.CountPagesAsync(WikiNamespace.System, includeDrafts: false)).IsEqualTo(1);

		var listed = await wiki.GetAllPagesAsync(0, 50, WikiNamespace.System);
		await Assert.That(listed.Select(p => p.Id)).IsEquivalentTo(new[] { published.Id, draft.Id });
		await Assert.That((await wiki.GetByIdAsync(draft.Id)).AsT0.Published).IsFalse();
		await Assert.That((await wiki.GetByCategoryAsync("lore")).Select(p => p.Id)).IsEquivalentTo(new[] { draft.Id });
		await Assert.That((await wiki.GetByTagAsync("LORE")).Select(p => p.Id)).IsEquivalentTo(new[] { draft.Id });

		await wiki.UpsertTranslationAsync(
			published.Id, "de", "Entwurf", "korpus", "#2", null, published: false, expectedRevisionNumber: null);
		await Assert.That((await wiki.GetTranslationsAsync(published.Id)).Single().Published).IsFalse();
		await Assert.That((await wiki.GetAllTranslationsAsync()).Single().Published).IsFalse();
	});

	[Test]
	public async Task DeleteRemovesThePageItsSlugEntryItsRevisionsAndItsTranslations() => await WithDatabaseAsync(async (db, wiki) =>
	{
		var page = (await wiki.CreateAsync("Cascade Me", "v1", "#1", WikiNamespace.Main, "lore", "en")).AsT0;
		var keep = (await wiki.CreateAsync("Untouched", "v1", "#1", WikiNamespace.Main, "lore", "en")).AsT0;
		await wiki.UpdateAsync(page.Id, "v2", "#1");
		await wiki.UpsertTranslationAsync(page.Id, "fr", "T", "fr", "#2", null, true, expectedRevisionNumber: null);
		await wiki.UpsertTranslationAsync(page.Id, "de", "T", "de", "#2", null, true, expectedRevisionNumber: null);
		await wiki.UpsertTranslationAsync(keep.Id, "fr", "T", "fr", "#2", null, true, expectedRevisionNumber: null);

		await Assert.That((await wiki.DeleteAsync(page.Id, "#1")).IsT0).IsTrue();

		var (pages, slug, revisions, translations) = db.Store.Read(tx => (
			tx.Range(Tables.WikiPage, []).Count(),
			tx.TryGet(Tables.WikiSlug, SlugKey("main", "lore", "cascade_me"), out _),
			tx.Range(Tables.WikiRev, PagePrefix(page.Id)).Count(),
			tx.Range(Tables.WikiTr, PagePrefix(page.Id)).Count()));

		await Assert.That(pages).IsEqualTo(1).Because("only the untouched page's row survives");
		await Assert.That(slug).IsFalse();
		await Assert.That(revisions).IsEqualTo(0);
		await Assert.That(translations).IsEqualTo(0);
		await Assert.That((await wiki.GetByIdAsync(page.Id)).IsT1).IsTrue();

		// The neighbour keeps everything: the cascade sweeps one page's prefix, not the whole table.
		await Assert.That((await wiki.GetRevisionsAsync(keep.Id)).Count).IsEqualTo(1);
		await Assert.That((await wiki.GetTranslationsAsync(keep.Id)).Count).IsEqualTo(1);
		await Assert.That((await wiki.DeleteAsync(page.Id, "#1")).IsT1).IsTrue();

		// The identity is free again once the index entry is gone.
		await Assert.That((await wiki.CreateAsync("Cascade Me", "fresh", "#1", WikiNamespace.Main, "lore")).IsT0).IsTrue();
	});
}
