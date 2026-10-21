using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>
/// Rebuilding the projection's secondary state into the other generation slot under continuous writes: the
/// flip publishes only a generation that matches the canonical facts, writes are journaled and replayed and
/// never dropped, and a rebuild that cannot catch up, or that a process left unfinished, is abandoned.
/// </summary>
public partial class IntegratedWikiMutationTests
{
	private static readonly SemanticWikiQuery LevelQuery = new("game", [new SemanticPredicate.IntegerRange("pf2e:level", 1, 10)], ["pf2e:level"], 7);

	/// <summary>Every row a generation owns is gone: it was cleared after a flip or an abandoned rebuild.</summary>
	private static async Task AssertGenerationCleared(LightningDatabase provider, ulong generation)
	{
		var tables = AtomicWikiLayout.ForGeneration(generation);
		var rows = provider.Store.Read(tx => tables.Owned.Sum(owned => tx.Range(owned.Table, owned.Prefix).Count()));
		await Assert.That(rows).IsEqualTo(0);
	}

	private static async Task AssertNoStaging(LightningDatabase provider)
	{
		var staging = provider.Store.Read(tx => new[] { 40, 41, 42, 43 }.Count(key => tx.TryGet(AtomicWikiLayout.Metadata, [(byte)key], out _))
			+ tx.Range(AtomicWikiLayout.Journal, []).Count());
		await Assert.That(staging).IsEqualTo(0);
	}

	/// <summary>Each reader's paged answer is the oracle's, for the levels the world's pages now hold.</summary>
	private static async Task AssertQueriesMatchOracle(QueryWorld world, Func<QueryPage, bool> holds)
	{
		foreach (var reader in QueryReaders)
			await Assert.That((await PageThroughAsync(world, reader, LevelQuery)).Ids)
				.IsEquivalentTo(await OracleAsync(world, reader, holds, byLevel: true), TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task ARebuildFlipsToAGenerationThatMatchesTheFactsAndRetiresTheOldOne()
	{
		await using var world = await QueryWorldAsync(spells: 12);
		var provider = world.Provider;
		var before = await SemanticWikiQueries.RunAsync(provider, _ => ValueTask.FromResult(AdminReader), LevelQuery with { PageSize = 2 }, null, CancellationToken.None);
		var token = ((SemanticQueryResult.Page)before).Continuation;
		await Assert.That(token).IsNotNull();

		await Assert.That(await provider.RebuildSemanticProjectionAsync(new(BatchSize: 3), CancellationToken.None))
			.IsEqualTo(new SemanticRebuildOutcome.Flipped(2));
		await Assert.That(provider.ActiveSemanticGeneration()).IsEqualTo(2UL);
		await AssertIndexesMatchFacts(provider);
		await AssertGenerationCleared(provider, 1);
		await AssertNoStaging(provider);
		await AssertQueriesMatchOracle(world, p => p.Level is >= 1 and <= 10);
		// A continuation names the generation it was issued under; after the flip it restarts.
		await Assert.That(await SemanticWikiQueries.RunAsync(provider, _ => ValueTask.FromResult(AdminReader), LevelQuery with { PageSize = 2 }, token,
			CancellationToken.None) is SemanticQueryResult.Restart).IsTrue();

		// A second rebuild fills the first slot again.
		await Assert.That(await provider.RebuildSemanticProjectionAsync(null, CancellationToken.None)).IsEqualTo(new SemanticRebuildOutcome.Flipped(3));
		await AssertIndexesMatchFacts(provider);
		await AssertGenerationCleared(provider, 2);
		await AssertQueriesMatchOracle(world, p => p.Level is >= 1 and <= 10);
		await world.Host.CloseAsync();
		await world.Host.OpenAsync();
		await Assert.That(world.Provider.ActiveSemanticGeneration()).IsEqualTo(3UL);
		await AssertIndexesMatchFacts(world.Provider);
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task ARebuildRepairsIndexRowsThatDriftedFromTheFacts()
	{
		await using var world = await QueryWorldAsync(spells: 6);
		var provider = world.Provider;
		await provider.Store.WriteAsync(tx => tx.Delete(AtomicWikiLayout.Forward, tx.Range(AtomicWikiLayout.Forward, []).First().Key));
		await Assert.That(async () => await AssertIndexesMatchFacts(provider)).Throws<TUnit.Assertions.Exceptions.AssertionException>();
		await Assert.That(await provider.RebuildSemanticProjectionAsync(null, CancellationToken.None) is SemanticRebuildOutcome.Flipped).IsTrue();
		await AssertIndexesMatchFacts(provider);
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task WritesDuringARebuildAreJournaledReplayedAndNeverDropped()
	{
		await using var world = await QueryWorldAsync(spells: 12);
		var provider = world.Provider;
		var wiki = world.Host.Services.GetRequiredService<IWikiService>();
		var spells = world.Pages.Values.Where(p => p.Order >= 0).OrderBy(p => p.Order).ToArray();
		var edited = spells[0];
		var reimported = spells[1];
		var deleted = spells[2];
		WikiPage? created = null;
		var replays = 0;
		provider.SemanticRebuildHook = async stage =>
		{
			if (stage != SemanticRebuildStage.ReplayCommitted || ++replays != 1) return;
			// Every subject is already copied: only the journal can carry these into the staged generation. An
			// edit drops facts, an import changes them, a delete tombstones, and a create adds a subject.
			await wiki.UpdateAsync(edited.Id, "# Edited during the rebuild", "#1");
			await provider.ImportSemanticFixtureFactsAsync([new(reimported.Id, new(AtomicWikiLayout.Schema.Version,
				[new(new(reimported.Id), "pf2e:level", 9L, QueryEvidence)]))], CancellationToken.None);
			await wiki.DeleteAsync(deleted.Id, "#1");
			created = (await wiki.CreateAsync("Created during the rebuild", "# New", "#1")).Value as WikiPage;
		};
		await Assert.That(await provider.RebuildSemanticProjectionAsync(new(BatchSize: 2), CancellationToken.None) is SemanticRebuildOutcome.Flipped).IsTrue();
		provider.SemanticRebuildHook = null;
		await Assert.That(created).IsNotNull();
		await AssertIndexesMatchFacts(provider);
		await AssertNoStaging(provider);
		await Assert.That(ObserveProjection(provider, edited.Id).Facts.Facts).IsEmpty();
		await Assert.That(ObserveProjection(provider, deleted.Id).State.State.Summary.Deleted).IsTrue();
		await Assert.That(ObserveProjection(provider, created!.Id).State.State.Summary.Deleted).IsFalse();
		// The oracle's facts after those writes; it reads the live pages, so the deleted one drops out by itself.
		var after = new QueryWorld(world.Host, [.. world.Pages.Values.Select(p => p with { Level = p.Id == edited.Id ? 0 : p.Id == reimported.Id ? 9 : p.Level })], "", "");
		await AssertQueriesMatchOracle(after, p => p.Level is >= 1 and <= 10);
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task AFullJournalAbandonsTheRebuildAndKeepsEveryWrite()
	{
		await using var world = await QueryWorldAsync(spells: 6);
		var provider = world.Provider;
		var wiki = world.Host.Services.GetRequiredService<IWikiService>();
		var spells = world.Pages.Values.Where(p => p.Order >= 0).OrderBy(p => p.Order).Take(3).ToArray();
		var fired = false;
		provider.SemanticRebuildHook = async stage =>
		{
			if (stage != SemanticRebuildStage.CheckpointCommitted || fired) return;
			fired = true;
			foreach (var page in spells) await wiki.UpdateAsync(page.Id, "# Edited past the journal", "#1");
		};
		var outcome = await provider.RebuildSemanticProjectionAsync(new(BatchSize: 2, JournalCapacity: 1), CancellationToken.None);
		provider.SemanticRebuildHook = null;
		await Assert.That(outcome is SemanticRebuildOutcome.Aborted { Reason: var reason } && reason.Contains("journal", StringComparison.Ordinal)).IsTrue();
		await Assert.That(provider.ActiveSemanticGeneration()).IsEqualTo(1UL);
		foreach (var page in spells)
			await Assert.That((await wiki.GetByIdAsync(page.Id)).Value is WikiPage { MarkdownSource: var body } && body.StartsWith("# Edited past", StringComparison.Ordinal)).IsTrue();
		await AssertIndexesMatchFacts(provider);
		await AssertNoStaging(provider);
		await AssertGenerationCleared(provider, 2);
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task WritesThatKeepOutrunningReplayAbandonTheRebuild()
	{
		await using var world = await QueryWorldAsync(spells: 4);
		var provider = world.Provider;
		var wiki = world.Host.Services.GetRequiredService<IWikiService>();
		var victim = world.Pages.Values.First(p => p.Order >= 0);
		var writes = 0;
		// Every validation is followed by another write, so the flip always finds an unconsumed sequence.
		provider.SemanticRebuildHook = async stage =>
		{
			if (stage == SemanticRebuildStage.Validated) await wiki.UpdateAsync(victim.Id, $"# Edit {++writes}", "#1");
		};
		var outcome = await provider.RebuildSemanticProjectionAsync(new(MaxReplayPasses: 2), CancellationToken.None);
		provider.SemanticRebuildHook = null;
		await Assert.That(outcome is SemanticRebuildOutcome.Aborted { Reason: var reason } && reason.Contains("outrunning", StringComparison.Ordinal)).IsTrue();
		await Assert.That(writes).IsEqualTo(3);
		await Assert.That((await wiki.GetByIdAsync(victim.Id)).Value is WikiPage { MarkdownSource: var body } && body.StartsWith("# Edit 3", StringComparison.Ordinal)).IsTrue();
		await Assert.That(provider.ActiveSemanticGeneration()).IsEqualTo(1UL);
		await AssertIndexesMatchFacts(provider);
		await AssertNoStaging(provider);
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task AReadAcrossStagedWritesIsUnaffectedAndAReadAcrossTheFlipIsDataChanged()
	{
		await using var world = await QueryWorldAsync(spells: 4);
		var provider = world.Provider;
		// Between a read's sections a rebuild stages, copies and replays, and stops short of the flip.
		var staged = await ProbeReadAsync(world.Host, async (_, ct) =>
		{
			provider.SemanticRebuildHook = stage => stage == SemanticRebuildStage.Validated
				? throw new OperationCanceledException("Stop before the flip.") : ValueTask.CompletedTask;
			try { await provider.RebuildSemanticProjectionAsync(new(BatchSize: 2), ct); }
			catch (OperationCanceledException) { }
			finally { provider.SemanticRebuildHook = null; }
		});
		// Staged rows advance no reader-visible version, so that read is still certified.
		await Assert.That(staged is SemanticReadResult<string>.Certified).IsTrue();
		await provider.DiscardIncompleteSemanticRebuildAsync(CancellationToken.None);

		var across = await ProbeReadAsync(world.Host, async (_, ct) =>
		{
			if (await provider.RebuildSemanticProjectionAsync(null, ct) is not SemanticRebuildOutcome.Flipped) throw new InvalidOperationException("The rebuild did not flip.");
		});
		await Assert.That(across is SemanticReadResult<string>.DataChanged).IsTrue();
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task AReadThatSeesTheActiveGenerationMoveIsDataChanged()
	{
		await using var world = await QueryWorldAsync(spells: 2);
		var provider = world.Provider;
		// Only the marker's generation moves, through a row no fixture version counts: the generation pin alone catches it.
		var moved = await ProbeReadAsync(world.Host, async (_, _) =>
		{
			var marker = provider.Store.Read(tx => AtomicWikiLayout.Decode(tx.TryGet(Tables.Meta, AtomicWikiLayout.MarkerKey, out var bytes) ? bytes : [], world.Host.FixtureId));
			await provider.Store.WriteAsync(tx => tx.Put(Tables.Meta, AtomicWikiLayout.MarkerKey, AtomicWikiLayout.Encode(marker with { Generation = 2 })));
		});
		await Assert.That(moved is SemanticReadResult<string>.DataChanged).IsTrue();
	}

	[Test]
	public async Task AStagedGenerationThatDisagreesWithTheReferenceIsNeverPublished()
	{
		await using var world = await QueryWorldAsync(spells: 6);
		var provider = world.Provider;
		var staged = AtomicWikiLayout.ForGeneration(2);
		provider.SemanticRebuildHook = async stage =>
		{
			// After the last replay and before validation, a staged index row goes missing.
			if (stage == SemanticRebuildStage.ReplayCommitted)
				await provider.Store.WriteAsync(tx => { if (tx.Range(staged.Forward, []).FirstOrDefault() is { Key: { } key }) tx.Delete(staged.Forward, key); });
		};
		var outcome = await provider.RebuildSemanticProjectionAsync(null, CancellationToken.None);
		provider.SemanticRebuildHook = null;
		await Assert.That(outcome is SemanticRebuildOutcome.Aborted { Reason: var reason } && reason.Contains("reference", StringComparison.Ordinal)).IsTrue();
		await Assert.That(provider.ActiveSemanticGeneration()).IsEqualTo(1UL);
		await AssertGenerationCleared(provider, 2);
		await AssertIndexesMatchFacts(provider);
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task AnUnfinishedRebuildIsDiscardedWhenTheWorldReopens()
	{
		await using var world = await QueryWorldAsync(spells: 6);
		var provider = world.Provider;
		provider.SemanticRebuildHook = stage => stage == SemanticRebuildStage.CheckpointCommitted
			? throw new InvalidOperationException("The rebuild's process stopped here.") : ValueTask.CompletedTask;
		await Assert.That(async () => await provider.RebuildSemanticProjectionAsync(new(BatchSize: 2), CancellationToken.None))
			.Throws<InvalidOperationException>();
		provider.SemanticRebuildHook = null;
		await Assert.That(provider.Store.Read(tx => tx.TryGet(AtomicWikiLayout.Metadata, [SemanticRebuildJournal.StagingKey], out _))).IsTrue();
		await world.Host.CloseAsync();
		await world.Host.OpenAsync();
		await Assert.That(world.Provider.ActiveSemanticGeneration()).IsEqualTo(1UL);
		await AssertNoStaging(world.Provider);
		await AssertGenerationCleared(world.Provider, 2);
		await AssertIndexesMatchFacts(world.Provider);
		await world.Host.CloseAsync();
	}
}
