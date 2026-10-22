using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>
/// Optional exact counts and enum facets (spec §7): asked for explicitly, computed under their own budget and
/// one aggregate at a time, exact only after every authorized match was seen, and otherwise Unknown with no
/// partial total. The page itself never depends on the aggregate.
/// </summary>
public partial class IntegratedWikiMutationTests
{
	private static SemanticWikiQuery Counted(SemanticWikiQuery query) => query with { IncludeCount = true, Facets = ["pf2e:tradition"] };

	/// <summary>What the oracle says the count and the tradition buckets are, buckets in keyword order.</summary>
	private static async Task<(long Count, string Buckets)> OracleAggregateAsync(QueryWorld world, WikiReader reader, Func<QueryPage, bool> holds, ISet<string> edited)
	{
		var ids = await OracleAsync(world, reader, holds, false, edited);
		var buckets = ids.SelectMany(id => world.Pages[id].Traditions.Distinct()).GroupBy(t => t).OrderBy(g => g.Key, StringComparer.Ordinal)
			.Select(g => $"{g.Key}:{g.Count()}");
		return (ids.Count, string.Join(",", buckets));
	}

	private static string Buckets(SemanticAggregate.Exact exact)
		=> string.Join(",", exact.Facets.Single().Buckets.Select(b => $"{b.Value}:{b.Count}"));

	[Test]
	[Arguments(11)]
	[Arguments(47)]
	public async Task CountsAndFacetsMatchTheOracleForEveryReaderAndShape(int seed)
	{
		var world = await RandomQueryWorldAsync(seed, subjects: 90);
		await using var _ = world.World;
		var random = new Random(seed);
		var compared = 0;
		foreach (var reader in QueryReaders)
			foreach (var (query, holds, _, target) in RandomShapes(world, random))
			{
				if (target is not null && !await TargetVisibleAsync(world.World, reader, target)) continue;
				var because = $"seed {seed}, {string.Join(",", reader.Scopes)}, {string.Join(" & ", query.Predicates)}";
				var (count, buckets) = await OracleAggregateAsync(world.World, reader, holds, world.Edited);
				string? token = null;
				// Every page of the walk carries the same exact aggregate over the whole answer.
				do
				{
					var result = await SemanticWikiQueries.RunAsync(world.World.Provider, "#test", _ => ValueTask.FromResult(reader), Counted(query), token,
						CancellationToken.None);
					if (result is not SemanticQueryResult.Page { Aggregate: SemanticAggregate.Exact exact } page)
						throw new InvalidOperationException($"{because}: expected an exact aggregate, got {result}.");
					await Assert.That(exact.Count).IsEqualTo(count).Because(because);
					await Assert.That(Buckets(exact)).IsEqualTo(buckets).Because(because);
					token = page.Continuation;
				} while (token is not null);
				compared++;
			}
		await Assert.That(compared).IsGreaterThan(40);
		await world.World.Host.CloseAsync();
	}

	[Test]
	public async Task NoAggregateIsComputedUnlessAskedFor()
	{
		await using var world = await QueryWorldAsync(spells: 6);
		var result = await SemanticWikiQueries.RunAsync(world.Provider, "#test", _ => ValueTask.FromResult(AdminReader), LevelQuery, null, CancellationToken.None);
		await Assert.That(result is SemanticQueryResult.Page { Aggregate: null }).IsTrue();
		await Assert.That(await SemanticWikiQueries.RunAsync(world.Provider, "#test", _ => ValueTask.FromResult(AdminReader), LevelQuery with { IncludeCount = true },
			null, CancellationToken.None) is SemanticQueryResult.Page { Aggregate: SemanticAggregate.Exact { Count: > 0, Facets.Count: 0 } }).IsTrue();
		await world.Host.CloseAsync();
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task AnAggregateOverItsBudgetIsUnknownAndThePageStillAnswers(bool subjects)
	{
		await using var world = await QueryWorldAsync(spells: 24);
		var expected = await OracleAsync(world, AdminReader, p => p.Level is >= 1 and <= 10, true);
		var budget = subjects ? new SemanticAggregateBudgets(Subjects: 3, Probes: 16_384) : new SemanticAggregateBudgets(Subjects: 4_096, Probes: 3);
		var result = await SemanticWikiQueries.RunAsync(world.Provider, "#test", _ => ValueTask.FromResult(AdminReader), Counted(LevelQuery), null,
			CancellationToken.None, aggregateBudgets: budget);
		if (result is not SemanticQueryResult.Page page) throw new InvalidOperationException($"Expected a page, got {result}.");
		// No partial total: it names the budget, not how far it got.
		await Assert.That(page.Aggregate).IsEqualTo(new SemanticAggregate.Unknown(subjects ? "subjects" : "probes"));
		await Assert.That(page.Rows.Select(r => r.PageId).ToList())
			.IsEquivalentTo(expected.Take(LevelQuery.PageSize).ToList(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task AReaderResolvedAfreshForEachReadStillGetsAnExactAggregate()
	{
		await using var world = await QueryWorldAsync(spells: 6);
		// A host resolves the reader's grants for every read; each answer is a new, equal reader.
		var result = await SemanticWikiQueries.RunAsync(world.Provider, "#test", _ => ValueTask.FromResult(WikiReader.From(AdminReader.Scopes, AdminReader.Dbref)),
			Counted(LevelQuery), null, CancellationToken.None);
		await Assert.That(result is SemanticQueryResult.Page { Aggregate: SemanticAggregate.Exact }).IsTrue().Because($"got {result}");
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task OnlyOneAggregateRunsAtATime()
	{
		await using var world = await QueryWorldAsync(spells: 6);
		var provider = world.Provider;
		using (var held = provider.SemanticQueries.TryEnterAggregate())
		{
			await Assert.That(held).IsNotNull();
			var busy = await SemanticWikiQueries.RunAsync(provider, "#test", _ => ValueTask.FromResult(AdminReader), Counted(LevelQuery), null, CancellationToken.None);
			await Assert.That(busy is SemanticQueryResult.Page { Aggregate: SemanticAggregate.Unknown { Reason: "busy" } }).IsTrue();
		}
		await Assert.That(await SemanticWikiQueries.RunAsync(provider, "#test", _ => ValueTask.FromResult(AdminReader), Counted(LevelQuery), null,
			CancellationToken.None) is SemanticQueryResult.Page { Aggregate: SemanticAggregate.Exact }).IsTrue();
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task AnAggregateIsUnknownWhenTheWorldChangesAfterThePageWasRead()
	{
		await using var world = await QueryWorldAsync(spells: 6);
		var wiki = world.Host.Services.GetRequiredService<IWikiService>();
		var victim = world.Pages.Values.First(p => p.Order >= 0).Id;
		var calls = 0;
		// The page read pauses twice between its three sections; the third pause is inside the aggregate's read.
		var result = await SemanticWikiQueries.RunAsync(world.Provider, "#test", _ => ValueTask.FromResult(AdminReader), Counted(LevelQuery), null,
			CancellationToken.None, between: async (_, _) =>
			{
				if (++calls == 3) await wiki.UpdateAsync(victim, "# Changed under the count", "#1");
			});
		await Assert.That(calls).IsGreaterThanOrEqualTo(3);
		await Assert.That(result is SemanticQueryResult.Page { Aggregate: SemanticAggregate.Unknown { Reason: "changed" } }).IsTrue().Because($"got {result}");
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task FacetsAcceptOnlyRegisteredEnumProperties()
	{
		var query = LevelQuery with { IncludeCount = true };
		await Assert.That(SemanticWikiQueries.Validate(query with { Facets = ["pf2e:tradition"] })).IsNull();
		await Assert.That(SemanticWikiQueries.Validate(query with { Facets = ["pf2e:tradition", "pf2e:source-book"] })).IsNull();
		foreach (var facets in new[] { ["pf2e:level"], ["pf2e:class"], ["pf2e:unknown"], new[] { "pf2e:tradition", "pf2e:tradition" } })
			await Assert.That(SemanticWikiQueries.Validate(query with { Facets = facets })).IsNotNull().Because(string.Join(",", facets));
	}
}
