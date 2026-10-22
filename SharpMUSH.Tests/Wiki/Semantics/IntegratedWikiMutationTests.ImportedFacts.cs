using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>Imported facts leave with their page, and a read that ended early never reads as a refinement.</summary>
public partial class IntegratedWikiMutationTests
{
	[Test]
	public async Task DeletingAnImportedPageRemovesItsFactsAndTheQueryNoLongerReturnsIt()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		await host.ActivateAtomicWikiProjectionAsync();
		if (!Settled(host)) throw new InvalidOperationException("Activation did not certify the empty package history.");
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var target = (await wiki.CreateAsync("Imported delete target", "# Target", "#1", WikiNamespace.Main, null, ["Spells"])).Expect<WikiPage>();
		var neighbor = (await wiki.CreateAsync("Imported delete neighbor", "# Neighbor", "#1", WikiNamespace.Main, null, ["Spells"])).Expect<WikiPage>();
		var beforeImport = Task3Snapshot(provider);
		await Assert.That(await provider.ImportSemanticFixtureFactsAsync([new(target.Id, new(AtomicWikiLayout.Schema.Version,
			[new(new(target.Id), "pf2e:level", 4L, QueryEvidence)]))], CancellationToken.None)).IsEqualTo(1);

		var world = new QueryWorld(host, [new(target.Id, target.Title, 0, 4, [], null), new(neighbor.Id, neighbor.Title, 1, 0, [], null)], "", "");
		var query = Query(new SemanticPredicate.IntegerEquals("pf2e:level", 4));
		var oracle = await OracleAsync(world, AdminReader, p => p.Level == 4, false);
		await Assert.That(oracle).IsEquivalentTo([target.Id]);
		await Assert.That((await PageThroughAsync(world, AdminReader, query)).Ids).IsEquivalentTo(oracle);

		var previous = ObserveProjection(provider, target.Id);
		await Assert.That(previous.State.State.ClassId).IsNotEqualTo(0UL);
		var before = Task3Snapshot(provider);
		var sequence = ObserveSequence(provider);
		var receipts = ObserveMutationCount(provider);
		var allocator = ObserveWikiCounter(provider);
		await Assert.That(await wiki.DeleteAsync(target.Id, "#1") is None).IsTrue();
		await AssertDeleteDelta(provider, target, previous, before, sequence, receipts, allocator, beforeImport);

		oracle = await OracleAsync(world, AdminReader, p => p.Level == 4, false);
		await Assert.That(oracle).IsEmpty();
		await Assert.That((await PageThroughAsync(world, AdminReader, query)).Ids).IsEquivalentTo(oracle);
		await host.CloseAsync();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task AReadThatEndedCancelledOrChangedIsNotARefinement(bool edit)
	{
		await using var world = await QueryWorldAsync();
		var wiki = world.Host.Services.GetRequiredService<IWikiService>();
		var victim = world.Pages.Values.First(p => p.Order >= 0).Id;
		using var cancellation = new CancellationTokenSource();
		// The budget that ARefinementIsAskedOnlyWhenTheBudgetIsReallyTooSmall exhausts in the seek section.
		var query = Query(new SemanticPredicate.KeywordEquals("pf2e:tradition", "arcane"), new SemanticPredicate.IntegerEquals("pf2e:level", 10));
		var result = await SemanticWikiQueries.RunAsync(world.Provider, "#test", _ => ValueTask.FromResult(AdminReader), query, null, cancellation.Token,
			budgets: new SemanticQueryBudgets(3, 8192), between: async (index, _) =>
			{
				if (index != 1) return;
				if (edit) await wiki.UpdateAsync(victim, "edited between sections", "#1");
				else await cancellation.CancelAsync();
			});
		await Assert.That(result is SemanticQueryResult.NotCertified { Outcome: var outcome }
			&& outcome.GetType().Name == (edit ? "DataChanged" : "Cancelled")).IsTrue();
		await world.Host.CloseAsync();
	}
}
