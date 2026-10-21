using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>
/// The world's storage can close or fail under a semantic read, a query or a rebuild. None of them keeps it
/// open or reports anything it half read: a read ends as storage closed or failed, a query is not certified
/// and its continuation restarts under the reopened world, and a rebuild aborts and is discarded on reopen.
/// </summary>
public partial class IntegratedWikiMutationTests
{
	private static Task<SemanticReadResult<string>> ProbeReadOnAsync(LightningDatabase provider,
		Func<SemanticReadSection, SemanticSectionStep<string>>? plan = null, Func<int, CancellationToken, ValueTask>? between = null)
		=> SemanticWikiReader.ReadAsync(provider, _ => ValueTask.FromResult(ProbeReader), 2, plan ?? ProbePlan, CancellationToken.None,
			between: between);

	/// <summary>Waits until <paramref name="closing"/> holds, giving up after about 30 seconds.</summary>
	private static async Task Until(Func<bool> closing)
	{
		for (var i = 0; i < 3000 && !closing(); i++) await Task.Delay(10);
		if (!closing()) throw new TimeoutException("The world never began to close.");
	}

	[Test]
	public async Task AWorldClosesBetweenSectionsAndTheReadEndsAsStorageClosed()
	{
		await using var host = await SettledHostAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var result = await ProbeReadOnAsync(provider, between: async (_, _) =>
		{
			// Activation still refuses while a read is admitted; closing does not.
			await Assert.That(async () => await host.ActivateAtomicWikiProjectionAsync()).Throws<SemanticFixtureOperationInProgressException>();
			await host.CloseAsync();
		});
		await Assert.That(result).IsEqualTo(SemanticReadResult<string>.StorageClosed.Value);
		await Assert.That(await ProbeReadOnAsync(provider)).IsEqualTo(SemanticReadResult<string>.StorageClosed.Value);
	}

	[Test]
	public async Task ACloseWaitsForTheSectionInProgressAndTheReadEndsAsStorageClosed()
	{
		await using var host = await SettledHostAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		using var entered = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		var read = Task.Run(() => ProbeReadOnAsync(provider, section =>
		{
			if (section.Index != 0) return SemanticSectionStep<string>.Answer("answer");
			entered.Set();
			release.Wait();
			return SemanticSectionStep<string>.Continue;
		}));
		await Until(() => entered.IsSet);
		var close = Task.Run(async () => await host.CloseAsync());
		await Until(() => provider.Store.DisposalWaitingForReaders);
		await Assert.That(close.IsCompleted).IsFalse();
		release.Set();
		await Assert.That(await read).IsEqualTo(SemanticReadResult<string>.StorageClosed.Value);
		await close;
		await Assert.That(provider.Store.IsClosed).IsTrue();
	}

	[Test]
	public async Task ASectionLmdbFailsEndsTheReadAsStorageFailedAndTheNextReadIsCertified()
	{
		await using var host = await SettledHostAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var failed = await ProbeReadOnAsync(provider, section => section.Index == 1
			? throw LightningStoreException.From(LightningDB.MDBResultCode.ReadersFull, "Semantic read")
			: SemanticSectionStep<string>.Continue);
		await Assert.That(failed).IsEqualTo(new SemanticReadResult<string>.StorageFailed(LightningDB.MDBResultCode.ReadersFull));
		await Assert.That(await ProbeReadOnAsync(provider) is SemanticReadResult<string>.Certified { Value: "answer" }).IsTrue();
		await host.CloseAsync();
	}

	[Test]
	public async Task AQueryOnAClosedWorldIsNotCertifiedAndItsContinuationRestartsOnceReopened()
	{
		await using var world = await QueryWorldAsync(spells: 8);
		var provider = world.Provider;
		var query = LevelQuery with { PageSize = 2 };
		var first = await SemanticWikiQueries.RunAsync(provider, _ => ValueTask.FromResult(AdminReader), query, null, CancellationToken.None);
		var token = ((SemanticQueryResult.Page)first).Continuation;
		await Assert.That(token).IsNotNull();

		await world.Host.CloseAsync();
		await Assert.That(await SemanticWikiQueries.RunAsync(provider, _ => ValueTask.FromResult(AdminReader), query, token, CancellationToken.None)
			is SemanticQueryResult.NotCertified { Outcome: var resumed } && resumed.GetType().Name == "StorageClosed").IsTrue();
		await Assert.That(await SemanticWikiQueries.RunAsync(provider, _ => ValueTask.FromResult(AdminReader), query, null, CancellationToken.None)
			is SemanticQueryResult.NotCertified { Outcome: var fresh } && fresh.GetType().Name == "StorageClosed").IsTrue();

		await world.Host.OpenAsync();
		await Assert.That(await SemanticWikiQueries.RunAsync(world.Provider, _ => ValueTask.FromResult(AdminReader), query, token,
			CancellationToken.None) is SemanticQueryResult.Restart).IsTrue();
		await Assert.That(await SemanticWikiQueries.RunAsync(world.Provider, _ => ValueTask.FromResult(AdminReader), query, null,
			CancellationToken.None) is SemanticQueryResult.Page).IsTrue();
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task ARebuildCutOffByACloseAbortsAndIsDiscardedWhenTheWorldReopens()
	{
		await using var world = await QueryWorldAsync(spells: 6);
		var provider = world.Provider;
		Task? close = null;
		provider.SemanticRebuildHook = async stage =>
		{
			if (stage != SemanticRebuildStage.CheckpointCommitted || close is not null) return;
			close = Task.Run(async () => await world.Host.CloseAsync());
			await Until(() => provider.SemanticStorageClosed);
		};
		var outcome = await provider.RebuildSemanticProjectionAsync(new(BatchSize: 2), CancellationToken.None);
		await Assert.That(outcome is SemanticRebuildOutcome.Aborted { Reason: var reason } && reason.Contains("closed", StringComparison.Ordinal)).IsTrue();
		await close!;

		await world.Host.OpenAsync();
		await Assert.That(world.Provider.ActiveSemanticGeneration()).IsEqualTo(1UL);
		await AssertNoStaging(world.Provider);
		await AssertGenerationCleared(world.Provider, 2);
		await AssertIndexesMatchFacts(world.Provider);
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task AFlipThatACloseCutsOffBeforeRetirementStandsAndTheNextRebuildClearsTheSlot()
	{
		await using var world = await QueryWorldAsync(spells: 6);
		var provider = world.Provider;
		Task? close = null;
		provider.SemanticRebuildHook = async stage =>
		{
			if (stage != SemanticRebuildStage.Flipped) return;
			close = Task.Run(async () => await world.Host.CloseAsync());
			await Until(() => provider.SemanticStorageClosed);
		};
		await Assert.That(await provider.RebuildSemanticProjectionAsync(new(BatchSize: 2), CancellationToken.None))
			.IsEqualTo(new SemanticRebuildOutcome.Flipped(2));
		await close!;

		await world.Host.OpenAsync();
		await Assert.That(world.Provider.ActiveSemanticGeneration()).IsEqualTo(2UL);
		await AssertIndexesMatchFacts(world.Provider);
		await Assert.That(await world.Provider.RebuildSemanticProjectionAsync(null, CancellationToken.None))
			.IsEqualTo(new SemanticRebuildOutcome.Flipped(3));
		await AssertGenerationCleared(world.Provider, 2);
		await AssertIndexesMatchFacts(world.Provider);
		await world.Host.CloseAsync();
	}
}
