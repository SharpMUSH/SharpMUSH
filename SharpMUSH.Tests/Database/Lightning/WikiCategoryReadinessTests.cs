using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Migration;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>Ordinary provider boundaries against a real, exclusively owned world, without a host/cache.</summary>
public class WikiCategoryReadinessTests
{
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private static async Task WithWorld(Func<LightningDatabase, string, Task> body)
	{
		// The parent runner sets disk-backed TMPDIR; keep this fixture self-contained even if it does not.
		var root = Path.Join(Directory.GetCurrentDirectory(), "work", "category-readiness-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		var db = Create(Path.Join(root, "live"));
		try { await body(db, root); }
		finally
		{
			await db.DisposeAsync();
			Directory.Delete(root, recursive: true);
		}
	}

	private static WikiPage Page(string title = "Member", string category = "a") => new(
		string.Empty, title.ToLowerInvariant(), title, "main", "body", "<p>body</p>", "body", "#42", "#42",
		DateTimeOffset.FromUnixTimeMilliseconds(1700000000000), DateTimeOffset.FromUnixTimeMilliseconds(1700000000000), 1)
	{ Categories = [category], Published = true, SourceLocale = "en" };

	private static async Task<WikiPage> Add(LightningDatabase db, string title = "Member", string category = "a")
		=> (await db.CreatePageAsync(Page(title, category))).Expect<WikiPage>();

	private static long Number(WikiPage page) => long.Parse(page.Id["wiki_page/".Length..]);

	private static async Task<Exception?> Failure(Func<Task> body)
	{
		try { await body(); return null; }
		catch (Exception error) { return error; }
	}

	private static string[] Snapshot(LightningDatabase db) => db.Store.Read(tx =>
		new[] { Tables.Meta, Tables.WikiPage, Tables.WikiSlug, Tables.WikiRev, Tables.WikiRecent,
			Tables.WikiByNamespace, Tables.WikiByCategory, Tables.WikiRequirement, Tables.WikiPin, Tables.WikiTr, Tables.PkgWiki }
		.SelectMany(table => tx.Range(table, []).Select(row => table.Name + ":" + Convert.ToHexString(row.Key)
			+ ":" + Convert.ToHexString(row.Value))).ToArray());

	private static (bool Valid, WikiCategoryMigrationPhase Phase, long Timestamp) CompleteAuthority(LightningDatabase db)
		=> db.Store.Read(tx =>
		{
			if (!tx.TryGet(Tables.Meta, WikiCategoryCheckpointCodec.StateKey, out var state))
				throw new InvalidDataException("Expected category migration state after Migrate.");
			if (!tx.TryGet(Tables.Meta, WikiCategoryCheckpointCodec.CompletionKey, out var completion))
				throw new InvalidDataException("Expected category migration completion marker after Migrate.");
			var valid = WikiCategoryAuthorityCodec.Validate(true, state, true, completion, db.Store.MaxKeySize);
			var checkpoint = WikiCategoryCheckpointCodec.Decode(state, db.Store.MaxKeySize);
			return (Valid: valid, Phase: checkpoint.Phase, Timestamp: checkpoint.CompletionUnixMs);
		});

	private static async Task Read(LightningDatabase db, int reader, WikiVisibility? visibility = null)
	{
		var admitted = visibility ?? WikiVisibility.All;
		switch (reader)
		{
			case 0: await db.GetPagesByCategoryAsync("a", 0, 10, admitted); break;
			case 1: await db.CountPagesInCategoryAsync("a", admitted); break;
			case 2: await db.CountPagesByCategoryAsync(admitted); break;
			case 3: await db.GetPagesByCategoryAsync("a", 0, 0, admitted); break;
			default: throw new ArgumentOutOfRangeException(nameof(reader));
		}
	}

	private static async Task Mutate(LightningDatabase db, int mutation, string id)
	{
		switch (mutation)
		{
			case 0: await db.CreatePageAsync(Page("New")); break;
			case 1: await db.UpdatePageBodyAsync(id, new WikiBody("changed", "changed", "changed"), "#43", "edit", DateTimeOffset.UtcNow); break;
			case 2: await db.SetPageTitleAsync(id, "Changed"); break;
			case 3: await db.SetPageMetadataAsync(id, ["b"], false); break;
			case 4: await db.DeletePageAsync(id); break;
			default: throw new ArgumentOutOfRangeException(nameof(mutation));
		}
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	public async Task ConstructedProviderRefusesEveryCategoryReadBeforeMigrateIncludingTakeZero(int reader)
		=> await WithWorld(async (db, _) =>
		{
			var before = Snapshot(db);
			var error = await Failure(() => Read(db, reader));
			await Assert.That(error is InvalidOperationException).IsTrue();
			await Assert.That(Snapshot(db)).IsEquivalentTo(before);
		});

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	[Arguments(4)]
	public async Task ReopenedUnmigratedProviderRefusesAllSourceMutationsBeforeAnyDurableChange(int mutation)
		=> await WithWorld(async (db, root) =>
		{
			await db.Migrate();
			var page = await Add(db);
			await db.DisposeAsync();
			await using var reopened = Create(Path.Join(root, "live"));
			var before = Snapshot(reopened);
			var error = await Failure(() => Mutate(reopened, mutation, page.Id));
			await Assert.That(error is InvalidOperationException).IsTrue();
			// Includes next_wiki, revisions, translations, source and every list index.
			await Assert.That(Snapshot(reopened)).IsEquivalentTo(before);
		});

	[Test]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	[Arguments(4)]
	public async Task MissingSourceDoesNotBypassUninitializedWriteGuard(int mutation)
		=> await WithWorld(async (db, _) =>
		{
			var before = Snapshot(db);
			var error = await Failure(() => Mutate(db, mutation, "wiki_page/999999"));
			await Assert.That(error is InvalidOperationException).IsTrue();
			await Assert.That(Snapshot(db)).IsEquivalentTo(before);
		});

	[Test]
	public async Task MigratePublishesReadyAndReopenedCompleteMigrationDoesNotRewriteAuthority()
		=> await WithWorld(async (db, root) =>
		{
			await db.Migrate();
			var authority = CompleteAuthority(db);
			await Assert.That(authority.Valid).IsTrue();
			await Assert.That(authority.Phase).IsEqualTo(WikiCategoryMigrationPhase.Complete);
			await Assert.That(authority.Timestamp > 0).IsTrue();
			var page = await Add(db);
			await Assert.That(await db.CountPagesInCategoryAsync("a", WikiVisibility.All)).IsEqualTo(1);
			await Assert.That((await db.CountPagesByCategoryAsync(WikiVisibility.All))["a"]).IsEqualTo(1);
			await Assert.That((await db.GetPagesByCategoryAsync("a", 0, 10, WikiVisibility.All)).Single().Id).IsEqualTo(page.Id);
			var before = Snapshot(db);
			await db.DisposeAsync();
			await using var reopened = Create(Path.Join(root, "live"));
			await reopened.Migrate();
			var reopenedAuthority = CompleteAuthority(reopened);
			await Assert.That(reopenedAuthority).IsEqualTo(authority);
			await Assert.That(Snapshot(reopened)).IsEquivalentTo(before);
			await Assert.That(await reopened.CountPagesInCategoryAsync("a", WikiVisibility.All)).IsEqualTo(1);
		});

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task StoreEpochChangeInvalidatesReadinessUntilMigrateEvenIfReplacementHasCompleteAuthority(bool swap)
		=> await WithWorld(async (db, root) =>
		{
			await db.Migrate();
			await Add(db, "Old");
			var oldEpoch = db.Store.Epoch;
			if (swap)
			{
				var incoming = Path.Join(root, "incoming");
				await using (var replacement = Create(incoming))
				{
					await replacement.Migrate();
					await Add(replacement, "Replacement");
				}
				db.Store.SwapDirectory(incoming, Path.Join(root, "previous"));
			}
			else db.Store.WipeDirectory();
			await Assert.That(db.Store.Epoch > oldEpoch).IsTrue();
			for (var reader = 0; reader < 4; reader++)
				await Assert.That(await Failure(() => Read(db, reader)) is InvalidOperationException).IsTrue();
			var before = Snapshot(db);
			await Assert.That(await Failure(() => Mutate(db, 0, string.Empty)) is InvalidOperationException).IsTrue();
			await Assert.That(Snapshot(db)).IsEquivalentTo(before);
			await db.Migrate();
			await Assert.That(await db.CountPagesInCategoryAsync("a", WikiVisibility.All)).IsEqualTo(swap ? 1 : 0);
			if (swap)
				await Assert.That((await db.GetPagesByCategoryAsync("a", 0, 10, WikiVisibility.All)).Single().Title).IsEqualTo("Replacement");
		});

	private static byte[] CorruptValue(string damage, byte[] prefix)
	{
		var value = WikiCategoryValueCodec.Value(prefix, false, "#43");
		switch (damage)
		{
			case "legacy": return [0, (byte)'#', (byte)'4', (byte)'3'];
			case "trailer": value[^1] ^= 1; break;
			case "publication": value[0] = 2; break;
			case "length": BinaryPrimitives.WriteUInt32BigEndian(value.AsSpan(value.Length - 8, 4), uint.MaxValue); break;
			case "utf8": value[1] = 0xff; break;
			case "label-utf8": break; // The caller corrupts the reached key's label instead.
			default: throw new ArgumentOutOfRangeException(nameof(damage));
		}
		return value;
	}

	[Test]
	[Arguments("legacy")]
	[Arguments("trailer")]
	[Arguments("publication")]
	[Arguments("length")]
	[Arguments("utf8")]
	[Arguments("label-utf8")]
	public async Task ReachedMalformedCategoryValueIsDecodedBeforeDraftExclusionOrAliasedLabelFiltering(string damage)
		=> await WithWorld(async (db, _) =>
		{
			await db.Migrate();
			// This valid longer label lies under the short category's native prefix, but is not its member.
			var prefix = WikiCategoryValueCodec.LabelPrefix("a\0b");
			var key = WikiCategoryValueCodec.Key(prefix, "Hidden alias", 9000);
			var value = CorruptValue(damage, prefix);
			if (damage == "label-utf8") key[2] = 0xff;
			await db.Store.WriteAsync(tx => tx.Put(Tables.WikiByCategory, key, value));
			var visibility = WikiVisibility.PublishedOnly with { ExcludedPageIds = new HashSet<string> { "wiki_page/9000" } };
			for (var reader = 0; reader < 3; reader++)
				await Assert.That(await Failure(() => Read(db, reader, visibility)) is InvalidDataException).IsTrue();
		});

	private static byte[] IncompleteState(int maxKeySize, bool building)
		=> WikiCategoryCheckpointCodec.Encode(new WikiCategoryMigrationCheckpoint(
			building ? WikiCategoryMigrationPhase.Building : WikiCategoryMigrationPhase.Clearing,
			1, 0, 0, 0, [], [], [], [], 0, 0), maxKeySize);

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ValidIncompleteAuthorityDoesNotRemainReadyAfterPreviouslySuccessfulMigrate(bool building)
		=> await WithWorld(async (db, _) =>
		{
			await db.Migrate();
			var page = await Add(db);
			var state = IncompleteState(db.Store.MaxKeySize, building);
			await db.Store.WriteAsync(tx =>
			{
				tx.Put(Tables.Meta, WikiCategoryCheckpointCodec.StateKey, state);
				tx.Delete(Tables.Meta, WikiCategoryCheckpointCodec.CompletionKey);
			});
			var before = Snapshot(db);
			for (var reader = 0; reader < 4; reader++)
				await Assert.That(await Failure(() => Read(db, reader)) is InvalidOperationException).IsTrue();
			for (var mutation = 0; mutation < 5; mutation++)
				await Assert.That(await Failure(() => Mutate(db, mutation, page.Id)) is InvalidOperationException).IsTrue();
			await Assert.That(Snapshot(db)).IsEquivalentTo(before);
		});

	[Test]
	public async Task UnrestrictedCountsUseIndexVisibilityWithoutReadingCorruptSourceBodies()
		=> await WithWorld(async (db, _) =>
		{
			await db.Migrate();
			var page = await Add(db);
			await db.Store.WriteAsync(tx => tx.Put(Tables.WikiPage, Keys.Dbref(Number(page)), "not json"u8));
			foreach (var visibility in new[] { WikiVisibility.All, WikiVisibility.PublishedOnly,
				new WikiVisibility(false, "#42"), WikiVisibility.All with { IncludedPageIds = new HashSet<string> { page.Id } } })
			{
				await Assert.That(await db.CountPagesInCategoryAsync("a", visibility)).IsEqualTo(1);
				await Assert.That((await db.CountPagesByCategoryAsync(visibility))["a"]).IsEqualTo(1);
			}
			var hidden = WikiVisibility.All with
			{
				Hidden = new WikiReadRestrictions(false, new HashSet<string> { "main" }, new HashSet<string>(), new HashSet<string>())
			};
			// Namespace/category restrictions require source metadata; JSON failure proves it was consulted.
			await Assert.That(await Failure(() => Read(db, 1, hidden)) is System.Text.Json.JsonException).IsTrue();
			await Assert.That(await Failure(() => Read(db, 2, hidden)) is System.Text.Json.JsonException).IsTrue();
		});

	[Test]
	public async Task CategoryWindowHydratesOnlyReturnedSources()
		=> await WithWorld(async (db, _) =>
		{
			await db.Migrate();
			var skipped = await Add(db, "A skipped");
			var returned = await Add(db, "B returned");
			var beyond = await Add(db, "C beyond");
			await db.Store.WriteAsync(tx =>
			{
				tx.Put(Tables.WikiPage, Keys.Dbref(Number(skipped)), "not json"u8);
				tx.Put(Tables.WikiPage, Keys.Dbref(Number(beyond)), "not json"u8);
			});
			await Assert.That((await db.GetPagesByCategoryAsync("a", 1, 1, WikiVisibility.All)).Single().Id).IsEqualTo(returned.Id);
			await Assert.That(await db.GetPagesByCategoryAsync("a", 0, 0, WikiVisibility.All)).IsEmpty();
			await Assert.That(await Failure(() => db.GetPagesByCategoryAsync("a", 0, 1, WikiVisibility.All)) is System.Text.Json.JsonException).IsTrue();
		});

	[Test]
	[Arguments("missing-state")]
	[Arguments("missing-marker")]
	[Arguments("bad-state")]
	[Arguments("bad-marker")]
	[Arguments("mismatched-marker")]
	public async Task CompleteAuthorityDamageFailsClosedForReadyReadsWritesAndMigrateWithoutGuessingRebuild(string damage)
		=> await WithWorld(async (db, _) =>
		{
			await db.Migrate();
			var page = await Add(db);
			await db.Store.WriteAsync(tx =>
			{
				switch (damage)
				{
					case "missing-state": tx.Delete(Tables.Meta, WikiCategoryCheckpointCodec.StateKey); break;
					case "missing-marker": tx.Delete(Tables.Meta, WikiCategoryCheckpointCodec.CompletionKey); break;
					case "bad-state": tx.Put(Tables.Meta, WikiCategoryCheckpointCodec.StateKey, "broken checkpoint"u8); break;
					case "bad-marker": tx.Put(Tables.Meta, WikiCategoryCheckpointCodec.CompletionKey, "broken marker"u8); break;
					case "mismatched-marker":
						tx.Put(Tables.Meta, WikiCategoryCheckpointCodec.CompletionKey,
						"{\"Id\":\"0004_wiki_category_value_v1\",\"AppliedUnixMs\":1}"u8); break;
					default: throw new ArgumentOutOfRangeException(nameof(damage));
				}
			});
			var before = Snapshot(db);
			for (var reader = 0; reader < 4; reader++)
				await Assert.That(await Failure(() => Read(db, reader)) is InvalidDataException).IsTrue();
			await Assert.That(await Failure(() => Mutate(db, 2, page.Id)) is InvalidDataException).IsTrue();
			await Assert.That(await Failure(() => db.Migrate().AsTask()) is InvalidDataException).IsTrue();
			await Assert.That(Snapshot(db)).IsEquivalentTo(before);
		});
}
