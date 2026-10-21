using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>
/// Supported semantic queries against an independent oracle that never reads the projection's indexes: the
/// oracle evaluates the imported facts with WikiAccessService decisions on the live pages, in creation order.
/// </summary>
public partial class IntegratedWikiMutationTests
{
	private static readonly FactProvenance QueryEvidence = new("fixture", "commit", "path", "field", false);

	/// <summary>A page the query world made, the facts imported for it, and its place in creation order.</summary>
	private sealed record QueryPage(string Id, string Title, int Order, long Level, ImmutableArray<string> Traditions, string? Class);

	private sealed class QueryWorld(SemanticWikiPrototypeHost host, IReadOnlyList<QueryPage> pages, string wizard, string cleric) : IAsyncDisposable
	{
		internal SemanticWikiPrototypeHost Host { get; } = host;
		internal Dictionary<string, QueryPage> Pages { get; } = pages.ToDictionary(p => p.Id);
		internal string Wizard { get; } = wizard;
		internal string Cleric { get; } = cleric;
		internal LightningDatabase Provider => Host.Services.GetRequiredService<LightningDatabase>();
		public ValueTask DisposeAsync() => Host.DisposeAsync();
	}

	private static readonly WikiReader PublicReader = WikiReader.From(["wiki.read"], "#5");
	private static readonly WikiReader CharacterReader = WikiReader.From(["wiki.read", "char.read"], "#5");
	private static readonly WikiReader SecretReader = WikiReader.From(["wiki.read", "char.read", "secret.read", "wiki.drafts"], "#5");
	private static readonly WikiReader AdminReader = WikiReader.From(["wiki.read", "wiki.admin", "wiki.drafts"], "#1");
	private static readonly WikiReader[] QueryReaders = [PublicReader, CharacterReader, SecretReader, AdminReader];

	private async Task<QueryWorld> QueryWorldAsync(int spells = 36)
	{
		var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		await wiki.SetRequirementsAsync(new WikiRuleTarget(WikiRuleScope.Namespace, "character"),
			new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Read] = ["char.read"] }, "#1");
		await wiki.SetRequirementsAsync(new WikiRuleTarget(WikiRuleScope.Category, "secret"),
			new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Read] = ["secret.read"] }, "#1");
		// Pages before activation, one guarded by its own page rule: only the per-candidate recheck sees it.
		var guarded = (await wiki.CreateAsync("Guarded Spell", "# Guarded", "#1", WikiNamespace.Main, null, ["Spells"])).Value is WikiPage g
			? g.Id : throw new InvalidOperationException("Could not create the guarded page.");
		var open = (await wiki.CreateAsync("Open Spell", "# Open", "#1", WikiNamespace.Main, null, ["Spells"])).Value is WikiPage o
			? o.Id : throw new InvalidOperationException("Could not create the open page.");
		await wiki.SetRequirementsAsync(new WikiRuleTarget(WikiRuleScope.Page, guarded),
			new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Read] = ["secret.read"] }, "#1");
		await host.ActivateAtomicWikiProjectionAsync();
		if (!Settled(host)) throw new InvalidOperationException("Activation did not certify the empty package history.");

		async Task<string> Create(string title, WikiNamespace ns, params string[] categories)
			=> (await wiki.CreateAsync(title, $"# {title}", "#1", ns, null, categories)).Value is WikiPage created
				? created.Id : throw new InvalidOperationException($"Could not create {title}.");
		var wizard = await Create("Wizard", WikiNamespace.Main);
		var cleric = await Create("Cleric", WikiNamespace.Main);
		var pages = new List<QueryPage> { new(guarded, "Guarded Spell", -2, 5, ["arcane"], null), new(open, "Open Spell", -1, 5, ["arcane"], null) };
		for (var i = 0; i < spells; i++)
		{
			var ns = i % 5 == 0 ? WikiNamespace.Character : WikiNamespace.Main;
			string[] categories = i % 7 == 0 ? ["Secret"] : ["Spells"];
			var title = $"Spell {i:00}";
			var id = await Create(title, ns, categories);
			if (i % 11 == 3) await wiki.SetMetadataAsync(id, categories, published: false);
			ImmutableArray<string> traditions = [.. new[] { i % 2 == 0 ? "arcane" : null, i % 3 == 0 ? "divine" : null }.OfType<string>()];
			pages.Add(new(id, title, i, i % 10 + 1, traditions, i % 2 == 0 ? wizard : i % 3 == 0 ? cleric : null));
		}

		var imports = pages.Select(p => new SemanticFixtureFactImport(p.Id, new(AtomicWikiLayout.Schema.Version,
		[
			new(new(p.Id), "pf2e:level", p.Level, QueryEvidence),
			.. p.Traditions.Select(t => new SemanticFact(new(p.Id), "pf2e:tradition", new Keyword(t), QueryEvidence)),
			.. p.Class is { } c ? [new SemanticFact(new(p.Id), "pf2e:class", new PageRef(c), QueryEvidence)] : Array.Empty<SemanticFact>()
		]))).ToList();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		await Assert.That(await provider.ImportSemanticFixtureFactsAsync(imports, CancellationToken.None)).IsEqualTo(spells + 2);
		return new QueryWorld(host, pages, wizard, cleric);
	}

	/// <summary>The oracle: imported facts, the live page and WikiAccessService, ordered as the query orders them.</summary>
	private static async Task<IReadOnlyList<string>> OracleAsync(QueryWorld world, WikiReader reader, Func<QueryPage, bool> holds,
		bool byLevel, ISet<string>? editedSinceImport = null)
	{
		var services = world.Host.Services;
		var wiki = services.GetRequiredService<IWikiService>();
		var access = services.GetRequiredService<IWikiAccessService>();
		var answer = new List<QueryPage>();
		foreach (var page in world.Pages.Values.OrderBy(p => p.Order))
		{
			if (editedSinceImport?.Contains(page.Id) == true || !holds(page)) continue;
			if (await wiki.GetByIdAsync(page.Id) is not WikiPage live || !await access.CanSeeAsync(reader, live)) continue;
			answer.Add(page);
		}
		IEnumerable<QueryPage> ordered = byLevel ? answer.OrderBy(p => p.Level).ThenBy(p => p.Order) : answer;
		return [.. ordered.Select(p => p.Id)];
	}

	private static async Task<(List<string> Ids, int Pages)> PageThroughAsync(QueryWorld world, WikiReader reader, SemanticWikiQuery query)
	{
		var ids = new List<string>();
		string? token = null;
		var pages = 0;
		do
		{
			var result = await SemanticWikiQueries.RunAsync(world.Provider, "#test", _ => ValueTask.FromResult(reader), query, token, CancellationToken.None);
			if (result is not SemanticQueryResult.Page page) throw new InvalidOperationException($"Expected a page, got {result}.");
			ids.AddRange(page.Rows.Select(r => r.PageId));
			token = page.Continuation;
			if (++pages > 100) throw new InvalidOperationException("Continuation did not terminate.");
		} while (token is not null);
		return (ids, pages);
	}

	private static SemanticWikiQuery Query(params SemanticPredicate[] predicates) => new("game", predicates, ["pf2e:level", "pf2e:class"], 7);

	[Test]
	public async Task ActivationAdmitsOnlyAnUntouchedOrASettledCoordinatedPackageHistory()
	{
		await using (var world = await QueryWorldAsync(spells: 1))
			await Assert.That(PackageState(world.Provider)[5]).IsEqualTo((byte)1);

		// Coordinated and settled: its operations are known to have ended.
		await using (var settled = NewHost())
		{
			await settled.OpenAsync();
			var installer = settled.Services.GetRequiredService<IPackageInstallService>();
			await Coordinated(settled, _ => installer.ApplyAsync(MatrixManifest(), MatrixRequest()));
			await Assert.That(Settled(settled)).IsTrue();
			await settled.ActivateAtomicWikiProjectionAsync();
			await Assert.That(Settled(settled)).IsTrue();
			await settled.CloseAsync();
		}

		// Direct installer work has no coordinator to prove it ended.
		await using var touched = NewHost();
		await touched.OpenAsync();
		await touched.Services.GetRequiredService<IPackageInstallService>().ApplyAsync(MatrixManifest(), MatrixRequest());
		await AssertFixtureFailure(() => touched.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
		await touched.CloseAsync();
	}

	[Test]
	public async Task EverySupportedShapeMatchesTheOracleForEveryReader()
	{
		await using var world = await QueryWorldAsync();
		var shapes = new (SemanticWikiQuery Query, Func<QueryPage, bool> Holds, bool ByLevel)[]
		{
			(Query(new SemanticPredicate.PageEquals("pf2e:class", world.Wizard)), p => p.Class == world.Wizard, false),
			(Query(new SemanticPredicate.KeywordEquals("pf2e:tradition", "Arcane")), p => p.Traditions.Contains("arcane"), false),
			(Query(new SemanticPredicate.IntegerRange("pf2e:level", 3, 6)), p => p.Level is >= 3 and <= 6, true),
			(Query(new SemanticPredicate.IntegerEquals("pf2e:level", 4)), p => p.Level == 4, false),
			(Query(new SemanticPredicate.PageEquals("pf2e:class", world.Cleric), new SemanticPredicate.KeywordEquals("pf2e:tradition", "divine")),
				p => p.Class == world.Cleric && p.Traditions.Contains("divine"), false),
			(Query(new SemanticPredicate.IntegerRange("pf2e:level", 2, 9), new SemanticPredicate.KeywordEquals("pf2e:tradition", "arcane")),
				p => p.Level is >= 2 and <= 9 && p.Traditions.Contains("arcane"), false),
			(SemanticLists.ArcaneByLevel.Query, p => p.Traditions.Contains("arcane") && p.Level is >= 1 and <= 10, true)
		};
		foreach (var reader in QueryReaders)
			foreach (var (query, holds, byLevel) in shapes)
			{
				var (ids, _) = await PageThroughAsync(world, reader, query);
				var expected = await OracleAsync(world, reader, holds, byLevel);
				await Assert.That(ids).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching)
					.Because($"{string.Join(",", reader.Scopes)} / {query.Predicates[0]}");
			}
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task AContinuationIsRefusedUnlessItsQueryReaderStateAndFixtureAllMatch()
	{
		await using var world = await QueryWorldAsync();
		var query = Query(new SemanticPredicate.KeywordEquals("pf2e:tradition", "arcane"));
		var first = (SemanticQueryResult.Page)await SemanticWikiQueries.RunAsync(world.Provider, "#test", _ => ValueTask.FromResult(SecretReader), query, null, CancellationToken.None);
		var token = first.Continuation!;
		var resumed = await SemanticWikiQueries.RunAsync(world.Provider, "#test", _ => ValueTask.FromResult(SecretReader), query, token, CancellationToken.None);
		await Assert.That(resumed is SemanticQueryResult.Page).IsTrue();

		async Task<SemanticQueryResult> Resume(WikiReader reader, SemanticWikiQuery q, string t, TimeProvider? clock = null, LightningDatabase? provider = null)
			=> await SemanticWikiQueries.RunAsync(provider ?? world.Provider, "#test", _ => ValueTask.FromResult(reader), q, t, CancellationToken.None, clock);
		// The same dbref with other scopes is another reader.
		await Assert.That(await Resume(WikiReader.From(["wiki.read", "char.read", "secret.read"], SecretReader.Dbref), query, token) is SemanticQueryResult.Restart).IsTrue();
		await Assert.That(await Resume(SecretReader, Query(new SemanticPredicate.KeywordEquals("pf2e:tradition", "divine")), token) is SemanticQueryResult.Restart).IsTrue();
		await Assert.That(await Resume(SecretReader, query, token[..^2] + (token[^2] == 'A' ? "BA" : "AA")) is SemanticQueryResult.Restart).IsTrue();
		await Assert.That(await Resume(SecretReader, query, token, new ShiftedClock(TimeSpan.FromMinutes(11))) is SemanticQueryResult.Restart).IsTrue();

		await using var other = await QueryWorldAsync(spells: 8);
		await Assert.That(await Resume(SecretReader, query, token, provider: other.Provider) is SemanticQueryResult.Restart).IsTrue();

		// A content change anywhere restarts the walk rather than mixing states.
		await world.Host.Services.GetRequiredService<IWikiService>().SetCategoryPinnedAsync("Spells", pinned: true);
		await Assert.That(await Resume(SecretReader, query, token) is SemanticQueryResult.Restart).IsTrue();
		await other.Host.CloseAsync();
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task AnEditDropsThePagesFactsInItsOwnJob()
	{
		await using var world = await QueryWorldAsync();
		var wiki = world.Host.Services.GetRequiredService<IWikiService>();
		var victim = world.Pages.Values.First(p => p.Order >= 0 && p.Traditions.Contains("arcane") && p.Order % 5 != 0 && p.Order % 7 != 0 && p.Order % 11 != 3);
		var query = Query(new SemanticPredicate.KeywordEquals("pf2e:tradition", "arcane"));
		var changed = await SemanticWikiQueries.RunAsync(world.Provider, "#test", _ => ValueTask.FromResult(PublicReader), query, null, CancellationToken.None,
			between: async (_, _) => { await wiki.UpdateAsync(victim.Id, "edited", "#1"); });
		await Assert.That(changed is SemanticQueryResult.NotCertified { Outcome: var outcome } && outcome.GetType().Name == "DataChanged").IsTrue();

		var (ids, _) = await PageThroughAsync(world, PublicReader, query);
		await Assert.That(ids).DoesNotContain(victim.Id);
		await Assert.That(ids).IsEquivalentTo(await OracleAsync(world, PublicReader, p => p.Traditions.Contains("arcane"), false,
			new HashSet<string> { victim.Id }), TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await world.Host.CloseAsync();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ARefinementIsAskedOnlyWhenTheBudgetIsReallyTooSmall(bool skips)
	{
		await using var world = await QueryWorldAsync();
		// Many arcane candidates, of which only level 10 survives the second predicate.
		var query = Query(new SemanticPredicate.KeywordEquals("pf2e:tradition", "arcane"), new SemanticPredicate.IntegerEquals("pf2e:level", 10));
		var budgets = skips ? new SemanticQueryBudgets(4096, 2) : new SemanticQueryBudgets(3, 8192);
		var result = await SemanticWikiQueries.RunAsync(world.Provider, "#test", _ => ValueTask.FromResult(AdminReader), query, null, CancellationToken.None, budgets: budgets);
		await Assert.That(result is SemanticQueryResult.RefineRequired { Budget: var budget } && budget == (skips ? "denied candidates" : "candidate probes")).IsTrue();
		// Honest: answering really needs more than that budget; the admin sees every arcane page.
		var arcane = world.Pages.Values.Count(p => p.Traditions.Contains("arcane"));
		var matching = world.Pages.Values.Count(p => p.Traditions.Contains("arcane") && p.Level == 10);
		await Assert.That(skips ? arcane - matching > 2 : arcane > 3).IsTrue();
		await Assert.That(await SemanticWikiQueries.RunAsync(world.Provider, "#test", _ => ValueTask.FromResult(AdminReader), query, null, CancellationToken.None)
			is SemanticQueryResult.Page).IsTrue();
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task UnsupportedShapesAreRejectedBeforeAnyRead()
	{
		var tooMany = Enumerable.Range(0, 5).Select(i => (SemanticPredicate)new SemanticPredicate.IntegerEquals("pf2e:level", i)).ToArray();
		SemanticWikiQuery[] rejected =
		[
			Query(tooMany),
			new("game", [new SemanticPredicate.IntegerEquals("pf2e:level", 1)], [], 0),
			new("game", [new SemanticPredicate.IntegerEquals("pf2e:level", 1)], [], 51),
			Query(new SemanticPredicate.IntegerEquals("pf2e:unknown", 1)),
			Query(new SemanticPredicate.IntegerRange("pf2e:tradition", 1, 2)),
			Query(new SemanticPredicate.IntegerRange("pf2e:level", 5, 1)),
			Query(new SemanticPredicate.IntegerRange("pf2e:level", long.MinValue, long.MaxValue)),
			Query(new SemanticPredicate.IntegerEquals("pf2e:level", 1)) with { OrderBy = "pf2e:level" },
			new("game", [new SemanticPredicate.IntegerEquals("pf2e:level", 1)], [.. Enumerable.Repeat("pf2e:level", 9)], 5)
		];
		foreach (var query in rejected)
			await Assert.That(SemanticWikiQueries.Validate(query)).IsNotNull();
	}

	[Test]
	public async Task ADeniedAndAMissingTargetAreTheSameUnavailableAnswer()
	{
		await using var world = await QueryWorldAsync();
		var hidden = world.Pages.Values.First(p => p.Order % 5 == 0).Id;
		var denied = await SemanticWikiQueries.RunAsync(world.Provider, "#test", _ => ValueTask.FromResult(PublicReader),
			Query(new SemanticPredicate.PageEquals("pf2e:class", hidden)), null, CancellationToken.None);
		var missing = await SemanticWikiQueries.RunAsync(world.Provider, "#test", _ => ValueTask.FromResult(PublicReader),
			Query(new SemanticPredicate.PageEquals("pf2e:class", "wiki:page:999999")), null, CancellationToken.None);
		await Assert.That(denied is SemanticQueryResult.NotCertified { Outcome: var a } && missing is SemanticQueryResult.NotCertified { Outcome: var b }
			&& ReferenceEquals(a, b)).IsTrue();
		await world.Host.CloseAsync();
	}

	[Test]
	public async Task ChangesConfinedToHiddenPagesDoNotChangeAPublicAnswer()
	{
		await using var world = await QueryWorldAsync();
		var query = Query(new SemanticPredicate.KeywordEquals("pf2e:tradition", "arcane"));
		var before = await PageThroughAsync(world, PublicReader, query);
		var wiki = world.Host.Services.GetRequiredService<IWikiService>();
		foreach (var hidden in world.Pages.Values.Where(p => p.Order % 5 == 0).Take(3)) await wiki.UpdateAsync(hidden.Id, "hidden edit", "#1");
		var after = await PageThroughAsync(world, PublicReader, query);
		await Assert.That(after.Ids).IsEquivalentTo(before.Ids, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await world.Host.CloseAsync();
	}

	private sealed class ShiftedClock(TimeSpan shift) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => System.GetUtcNow().Add(shift);
	}
}
