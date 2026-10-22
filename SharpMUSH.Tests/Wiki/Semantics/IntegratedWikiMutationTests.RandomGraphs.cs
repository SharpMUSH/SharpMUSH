using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>
/// Seeded random worlds against the oracle (phase C): every reader and every supported shape, paged at a random
/// size, over hidden, unpublished, rule-guarded, recategorised, edited and deleted subjects and targets, one hot
/// target most subjects point at, and levels at the edges of every range. The seeds are fixed, so a failure
/// replays.
/// </summary>
public partial class IntegratedWikiMutationTests
{
	private static readonly string[] RandomTraditions = ["arcane", "divine", "occult", "primal"];
	private static readonly long[] RandomLevels = [long.MinValue, -3, 0, 1, 2, 3, 5, 9, 10, 11, int.MaxValue];

	/// <summary>A random world: its subjects in <see cref="QueryWorld.Pages"/>, its class targets, and the subjects edited since import.</summary>
	private sealed record RandomWorld(QueryWorld World, IReadOnlyList<string> Targets, string Hot, ISet<string> Edited, IReadOnlyDictionary<string, string> Changes)
	{
		/// <summary>The pages one answer has and the other lacks, each with what the world did to it, for a failure message.</summary>
		internal string Explain(IEnumerable<string> actual, IEnumerable<string> expected)
			=> string.Join("; ", actual.Except(expected).Select(id => $"+{id}").Concat(expected.Except(actual).Select(id => $"-{id}"))
				.Select(d => $"{d} ({Changes.GetValueOrDefault(d[1..], "unchanged")})"));
	}

	private async Task<RandomWorld> RandomQueryWorldAsync(int seed, int subjects)
	{
		var random = new Random(seed);
		var changes = new Dictionary<string, string>();
		var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		await wiki.SetRequirementsAsync(new WikiRuleTarget(WikiRuleScope.Namespace, "character"),
			new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Read] = ["char.read"] }, "#1");
		await wiki.SetRequirementsAsync(new WikiRuleTarget(WikiRuleScope.Category, "secret"),
			new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Read] = ["secret.read"] }, "#1");
		await host.ActivateAtomicWikiProjectionAsync();
		if (!Settled(host)) throw new InvalidOperationException("Activation did not certify the empty package history.");

		async Task<string> Create(string title)
		{
			var ns = random.Next(6) == 0 ? WikiNamespace.Character : WikiNamespace.Main;
			string[] categories = random.Next(5) == 0 ? ["Secret"] : random.Next(3) == 0 ? [] : ["Spells"];
			var id = (await wiki.CreateAsync(title, $"# {title}", "#1", ns, null, categories)).Value is WikiPage created
				? created.Id : throw new InvalidOperationException($"Could not create {title}.");
			changes[id] = $"{ns} [{string.Join(",", categories)}]";
			if (random.Next(9) == 0)
			{
				await wiki.SetMetadataAsync(id, categories, published: false);
				changes[id] += " unpublished";
			}
			if (random.Next(11) == 0)
			{
				changes[id] += " page rule";
				await wiki.SetRequirementsAsync(new WikiRuleTarget(WikiRuleScope.Page, id),
					new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Read] = ["secret.read"] }, "#1");
			}
			return id;
		}

		var targets = new List<string>();
		for (var i = 0; i < 6; i++) targets.Add(await Create($"Class {seed}-{i}"));
		var hot = targets[random.Next(targets.Count)];
		var pages = new List<QueryPage>();
		for (var i = 0; i < subjects; i++)
		{
			var title = $"Subject {seed}-{i:000}";
			var id = await Create(title);
			var level = RandomLevels[random.Next(RandomLevels.Length)];
			ImmutableArray<string> traditions = [.. RandomTraditions.Where(_ => random.Next(3) == 0)];
			// Most subjects point at the hot target; a few at none.
			var target = random.Next(10) switch { < 6 => hot, < 9 => targets[random.Next(targets.Count)], _ => null };
			pages.Add(new(id, title, i, level, traditions, target));
		}

		var imports = pages.Select(p => new SemanticFixtureFactImport(p.Id, new(AtomicWikiLayout.Schema.Version,
		[
			.. p.Level == long.MinValue ? Array.Empty<SemanticFact>() : [new SemanticFact(new(p.Id), "pf2e:level", p.Level, QueryEvidence)],
			.. p.Traditions.Select(t => new SemanticFact(new(p.Id), "pf2e:tradition", new Keyword(t), QueryEvidence)),
			.. p.Class is { } c ? [new SemanticFact(new(p.Id), "pf2e:class", new PageRef(c), QueryEvidence)] : Array.Empty<SemanticFact>()
		]))).Where(i => !i.Facts.Facts.IsEmpty).ToList();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		await Assert.That(await provider.ImportSemanticFixtureFactsAsync(imports, CancellationToken.None)).IsEqualTo(imports.Count);

		// After import: deletes remove subjects and one target, and some pages move into or out of the secret
		// category. Any edit, of the body or only of the categories, drops the facts imported for the page.
		var edited = new HashSet<string>();
		foreach (var page in pages)
		{
			switch (random.Next(14))
			{
				case 0:
					await wiki.UpdateAsync(page.Id, "# Edited", "#1");
					changes[page.Id] += " edited";
					edited.Add(page.Id);
					break;
				case 1:
					await wiki.DeleteAsync(page.Id, "#1");
					changes[page.Id] += " deleted";
					break;
				case 2:
					await wiki.SetMetadataAsync(page.Id, ["Secret"], published: true);
					changes[page.Id] += " then [Secret] published";
					edited.Add(page.Id);
					break;
				case 3:
					await wiki.SetMetadataAsync(page.Id, ["Spells"], published: true);
					changes[page.Id] += " then [Spells] published";
					edited.Add(page.Id);
					break;
			}
		}
		var doomed = targets.First(t => t != hot);
		await wiki.DeleteAsync(doomed, "#1");
		return new(new QueryWorld(host, pages, hot, doomed), targets, hot, edited, changes);
	}

	/// <summary>Every shape, with what the oracle holds and whether a range drives the order.</summary>
	private static IEnumerable<(SemanticWikiQuery Query, Func<QueryPage, bool> Holds, bool ByLevel, string? Target)> RandomShapes(RandomWorld world, Random random)
	{
		SemanticWikiQuery Q(params SemanticPredicate[] predicates) => new("game", predicates, ["pf2e:level", "pf2e:class", "pf2e:tradition"], random.Next(1, 8));
		foreach (var target in world.Targets)
		{
			yield return (Q(new SemanticPredicate.PageEquals("pf2e:class", target)), p => p.Class == target, false, target);
			yield return (Q(new SemanticPredicate.PageEquals("pf2e:class", target), new SemanticPredicate.IntegerRange("pf2e:level", 1, 10),
				new SemanticPredicate.KeywordEquals("pf2e:tradition", "arcane")),
				p => p.Class == target && p.Level is >= 1 and <= 10 && p.Traditions.Contains("arcane"), false, target);
		}
		foreach (var tradition in RandomTraditions)
			yield return (Q(new SemanticPredicate.KeywordEquals("pf2e:tradition", tradition)), p => p.Traditions.Contains(tradition), false, null);
		foreach (var (low, high) in new[] { (1L, 10L), (-3L, 0L), (10L, 11L), (int.MaxValue, int.MaxValue) })
			yield return (Q(new SemanticPredicate.IntegerRange("pf2e:level", low, high)), p => p.Level >= low && p.Level <= high, true, null);
		yield return (Q(new SemanticPredicate.IntegerEquals("pf2e:level", 0)), p => p.Level == 0, false, null);
		yield return (Q(new SemanticPredicate.KeywordEquals("pf2e:tradition", "divine"), new SemanticPredicate.KeywordEquals("pf2e:tradition", "occult")),
			p => p.Traditions.Contains("divine") && p.Traditions.Contains("occult"), false, null);
		yield return (Q(new SemanticPredicate.IntegerRange("pf2e:level", 1, 10), new SemanticPredicate.KeywordEquals("pf2e:tradition", "primal")) with { OrderBy = "pf2e:level" },
			p => p.Level is >= 1 and <= 10 && p.Traditions.Contains("primal"), true, null);
		yield return (SemanticLists.ArcaneByLevel.Query, p => p.Traditions.Contains("arcane") && p.Level is >= 1 and <= 10, true, null);
	}

	/// <summary>Whether <paramref name="reader"/> may see the live target; a deleted one is unavailable to everyone.</summary>
	private static async Task<bool> TargetVisibleAsync(QueryWorld world, WikiReader reader, string target)
	{
		var services = world.Host.Services;
		return await services.GetRequiredService<IWikiService>().GetByIdAsync(target) is WikiPage live
			&& await services.GetRequiredService<IWikiAccessService>().CanSeeAsync(reader, live);
	}

	/// <summary>Pages a query through to its end; null when its first page is unavailable.</summary>
	private static async Task<List<string>?> PageThroughOrUnavailableAsync(QueryWorld world, WikiReader reader, SemanticWikiQuery query,
		SemanticQueryBudgets? budgets = null)
	{
		var ids = new List<string>();
		string? token = null;
		var pages = 0;
		do
		{
			var result = await SemanticWikiQueries.RunAsync(world.Provider, "#test", _ => ValueTask.FromResult(reader), query, token,
				CancellationToken.None, budgets: budgets);
			switch (result)
			{
				case SemanticQueryResult.Page page:
					ids.AddRange(page.Rows.Select(r => r.PageId));
					token = page.Continuation;
					break;
				case SemanticQueryResult.NotCertified { Outcome: var outcome } when pages == 0 && outcome.GetType().Name == "Unavailable":
					return null;
				default:
					throw new InvalidOperationException($"Expected a page, got {result}.");
			}
			if (++pages > 500) throw new InvalidOperationException("Continuation did not terminate.");
		} while (token is not null);
		return ids;
	}

	[Test]
	[Arguments(11)]
	[Arguments(23)]
	[Arguments(47)]
	public async Task RandomWorldsMatchTheOracleForEveryReaderAndShape(int seed)
	{
		var world = await RandomQueryWorldAsync(seed, subjects: 90);
		await using var _ = world.World;
		var random = new Random(seed * 7919);
		var compared = 0;
		foreach (var reader in QueryReaders)
			foreach (var (query, holds, byLevel, target) in RandomShapes(world, random))
			{
				var because = $"seed {seed}, {string.Join(",", reader.Scopes)}, {string.Join(" & ", query.Predicates)}, page {query.PageSize}";
				var actual = await PageThroughOrUnavailableAsync(world.World, reader, query);
				if (target is not null && !await TargetVisibleAsync(world.World, reader, target))
				{
					// A denied target and a deleted one answer alike, and neither shows what points at it.
					await Assert.That(actual).IsNull().Because(because);
					continue;
				}
				var expected = await OracleAsync(world.World, reader, holds, byLevel, world.Edited);
				await Assert.That(actual).IsNotNull().Because(because);
				await Assert.That(actual!).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching)
					.Because(because + ": " + world.Explain(actual!, expected));
				compared++;
			}
		await Assert.That(compared).IsGreaterThan(40);
		await world.World.Host.CloseAsync();
	}

	[Test]
	[Arguments(11)]
	[Arguments(23)]
	public async Task BudgetStopIsNotEmptyOrExactCount(int seed)
	{
		// Whatever a too-small budget stops, it never answers a short or an empty page in place of the real one.
		var world = await RandomQueryWorldAsync(seed, subjects: 60);
		await using var _ = world.World;
		var random = new Random(seed);
		var refined = 0;
		foreach (var reader in QueryReaders)
			foreach (var (query, holds, byLevel, target) in RandomShapes(world, random))
			{
				if (target is not null && !await TargetVisibleAsync(world.World, reader, target)) continue;
				var expected = await OracleAsync(world.World, reader, holds, byLevel, world.Edited);
				var budgets = new SemanticQueryBudgets(random.Next(1, 6), random.Next(0, 4));
				var result = await SemanticWikiQueries.RunAsync(world.World.Provider, "#test", _ => ValueTask.FromResult(reader), query, null,
					CancellationToken.None, budgets: budgets);
				var because = $"seed {seed}, {string.Join(",", reader.Scopes)}, {string.Join(" & ", query.Predicates)}, {budgets}";
				switch (result)
				{
					case SemanticQueryResult.RefineRequired:
						refined++;
						break;
					case SemanticQueryResult.Page page:
						// A page under a small budget is the oracle's first page, and says whether more follow.
						await Assert.That(page.Rows.Select(r => r.PageId).ToList())
							.IsEquivalentTo(expected.Take(query.PageSize).ToList(), TUnit.Assertions.Enums.CollectionOrdering.Matching)
							.Because(because + ": " + world.Explain(page.Rows.Select(r => r.PageId), expected.Take(query.PageSize)));
						await Assert.That(page.Continuation is not null).IsEqualTo(expected.Count > query.PageSize).Because(because);
						break;
					default:
						throw new InvalidOperationException($"{because}: unexpected {result}.");
				}
			}
		await Assert.That(refined).IsGreaterThan(0);
		await world.World.Host.CloseAsync();
	}

	[Test]
	public async Task DeniedSubjectsDoNotConsumeVisiblePostingBudget()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		await wiki.SetRequirementsAsync(new WikiRuleTarget(WikiRuleScope.Category, "secret"),
			new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Read] = ["secret.read"] }, "#1");
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var visible = new List<string>();
		var imports = new List<SemanticFixtureFactImport>();
		// Many arcane subjects a public reader cannot see, few it can.
		for (var i = 0; i < 120; i++)
		{
			var secret = i % 24 != 0;
			var id = (await wiki.CreateAsync($"Arcane {i:000}", "# Arcane", "#1", WikiNamespace.Main, null, secret ? ["Secret"] : ["Spells"])).Value is WikiPage created
				? created.Id : throw new InvalidOperationException("Could not create a subject.");
			if (!secret) visible.Add(id);
			imports.Add(new(id, new(AtomicWikiLayout.Schema.Version, [new(new(id), "pf2e:tradition", new Keyword("arcane"), QueryEvidence)])));
		}
		await provider.ImportSemanticFixtureFactsAsync(imports, CancellationToken.None);
		var query = new SemanticWikiQuery("game", [new SemanticPredicate.KeywordEquals("pf2e:tradition", "arcane")], [], 10);
		// Enough probes for the visible subjects alone, and no denied skips at all: the secret ones sit in a
		// visibility class the public reader's seek never enters.
		var result = await SemanticWikiQueries.RunAsync(provider, "#test", _ => ValueTask.FromResult(PublicReader), query, null, CancellationToken.None,
			budgets: new SemanticQueryBudgets(visible.Count + 1, 0));
		await Assert.That(result is SemanticQueryResult.Page { Continuation: null }).IsTrue().Because($"got {result}");
		await Assert.That(((SemanticQueryResult.Page)result).Rows.Select(r => r.PageId).ToList())
			.IsEquivalentTo(visible, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		// The secret reader sees all of them, so the same budget is really too small for it.
		await Assert.That(await SemanticWikiQueries.RunAsync(provider, "#test", _ => ValueTask.FromResult(SecretReader), query, null,
			CancellationToken.None, budgets: new SemanticQueryBudgets(visible.Count + 1, 0)) is SemanticQueryResult.RefineRequired).IsTrue();
		await host.CloseAsync();
	}
}
