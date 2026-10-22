using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>
/// Admission on a real issued world: a query waits for its principal's running query, a refused one never
/// reaches storage, and only one rebuild runs at a time.
/// </summary>
public partial class IntegratedWikiMutationTests
{
	[Test]
	public async Task AQueryWaitsForItsPrincipalsRunningQueryAndThenAnswers()
	{
		await using var world = await QueryWorldAsync(spells: 4);
		var provider = world.Provider;
		var held = await provider.SemanticQueries.EnterAsync("#5", CancellationToken.None) as SemanticQueryAdmission.Entry.Admitted;
		await Assert.That(held).IsNotNull();
		var waiting = SemanticWikiQueries.RunAsync(provider, "#5", _ => ValueTask.FromResult(PublicReader), LevelQuery, null, CancellationToken.None);
		await Assert.That(waiting.IsCompleted).IsFalse();
		// Another principal is not held up by #5.
		await Assert.That(await SemanticWikiQueries.RunAsync(provider, "#6", _ => ValueTask.FromResult(PublicReader), LevelQuery, null,
			CancellationToken.None) is SemanticQueryResult.Page).IsTrue();
		held!.Ticket.Dispose();
		await Assert.That(await waiting is SemanticQueryResult.Page).IsTrue();
		await Assert.That(provider.SemanticQueries.Snapshot().Active).IsEqualTo(0);
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task ARefusedQueryIsBusyBeforeItTouchesStorage()
	{
		await using var world = await QueryWorldAsync(spells: 2);
		var provider = world.Provider;
		var held = await provider.SemanticQueries.EnterAsync("#5", CancellationToken.None) as SemanticQueryAdmission.Entry.Admitted;
		using var cancelWaiters = new CancellationTokenSource();
		var waiters = new[] { provider.SemanticQueries.EnterAsync("#5", cancelWaiters.Token), provider.SemanticQueries.EnterAsync("#5", cancelWaiters.Token) };
		// A closed world would answer StorageClosed to any query that reached it; this one never does.
		await world.Host.CloseAsync();
		await Assert.That(await SemanticWikiQueries.RunAsync(provider, "#5", _ => ValueTask.FromResult(PublicReader), LevelQuery, null,
			CancellationToken.None)).IsEqualTo(new SemanticQueryResult.Busy("principal"));
		await cancelWaiters.CancelAsync();
		foreach (var waiter in waiters)
			await Assert.That(async () => await waiter).Throws<OperationCanceledException>();
		held!.Ticket.Dispose();
	}

	[Test]
	public async Task ARebuildStartedWhileAnotherRunsIsRefusedAndLeavesItsStagingAlone()
	{
		await using var world = await QueryWorldAsync(spells: 12);
		var provider = world.Provider;
		SemanticRebuildOutcome? second = null;
		provider.SemanticRebuildHook = async stage =>
		{
			if (stage != SemanticRebuildStage.CheckpointCommitted || second is not null) return;
			second = await provider.RebuildSemanticProjectionAsync(null, CancellationToken.None);
		};
		await Assert.That(await provider.RebuildSemanticProjectionAsync(new(BatchSize: 3), CancellationToken.None))
			.IsEqualTo(new SemanticRebuildOutcome.Flipped(2));
		provider.SemanticRebuildHook = null;
		await Assert.That(second).IsEqualTo(SemanticRebuildOutcome.AlreadyRunning.Value);
		await AssertIndexesMatchFacts(provider);
		await AssertNoStaging(provider);
		await AssertQueriesMatchOracle(world, p => p.Level is >= 1 and <= 10);
		// The next rebuild, once the first has ended, is admitted.
		await Assert.That(await provider.RebuildSemanticProjectionAsync(null, CancellationToken.None)).IsEqualTo(new SemanticRebuildOutcome.Flipped(3));
		await world.Host.CloseAsync();
	}
}
