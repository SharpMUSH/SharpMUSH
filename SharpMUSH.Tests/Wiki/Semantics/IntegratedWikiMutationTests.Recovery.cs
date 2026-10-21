using System.Diagnostics;
using System.Text;
using System.Text.Json;
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
/// Kill points: a child process seeds an issued world and blocks at one named point, the test kills it with
/// SIGKILL, adopts the world and checks it. Canonical facts are what the last durable commit left, every index
/// row is what those facts imply, an earlier continuation restarts, and no reader sees a row it may not.
/// </summary>
public partial class IntegratedWikiMutationTests
{
	private static string RecoveryRoot => Path.Join(Path.GetTempPath(), "semantic-wiki-first-atomic", "recovery");

	private sealed record KilledWorld(SemanticRecoveryEvidence Evidence, string EvidencePath);

	/// <summary>Runs <paramref name="scenario"/> in a child test process and kills it once it reports <paramref name="phase"/>.</summary>
	private async Task<KilledWorld> KillChildAtAsync(string scenario, string phase)
	{
		var evidence = Path.Join(RecoveryRoot, "evidence-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(evidence);
		var spec = Path.Join(evidence, "spec.json");
		File.WriteAllText(spec, JsonSerializer.Serialize(new SemanticRecoverySpec(scenario, ScratchRoot, MySqlTestServer.Instance.GetConnectionString(),
			$"nats://localhost:{NatsTestServer.Instance.GetMappedPublicPort(4222)}", ConfigFile, evidence)));
		var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
		var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
		if (string.IsNullOrWhiteSpace(host)) host = string.IsNullOrWhiteSpace(root) ? "dotnet" : Path.Join(root, "dotnet");
		var start = new ProcessStartInfo(host) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
		start.ArgumentList.Add(Path.Join(AppContext.BaseDirectory, "SharpMUSH.Tests.dll"));
		foreach (var arg in new[] { "--treenode-filter", $"/*/*/{nameof(SemanticRecoveryChild)}/*", "--no-ansi" }) start.ArgumentList.Add(arg);
		start.Environment[SemanticRecoveryChild.Variable] = spec;
		using var process = Process.Start(start) ?? throw new InvalidOperationException("The recovery child did not start.");
		using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
		var errors = process.StandardError.ReadToEndAsync(deadline.Token);
		var seeded = false;
		var output = new StringBuilder();
		try
		{
			while (await process.StandardOutput.ReadLineAsync(deadline.Token) is { } line)
			{
				if (output.Length < 64 * 1024) output.AppendLine(line);
				if (!line.StartsWith(SemanticRecoveryChild.Prefix, StringComparison.Ordinal)) continue;
				using var record = JsonDocument.Parse(line[SemanticRecoveryChild.Prefix.Length..]);
				var entry = record.RootElement;
				if (entry.GetProperty("pid").GetInt32() != process.Id || entry.GetProperty("scenario").GetString() != scenario)
					throw new InvalidDataException("A recovery checkpoint came from another process.");
				var observed = entry.GetProperty("phase").GetString();
				if (observed == "seeded") { seeded = true; continue; }
				if (observed != phase || !seeded) throw new InvalidDataException($"Unexpected checkpoint {observed}.");
				// Only this owned Process handle, never a PID lookup or a broad process match.
				process.Kill();
				await process.WaitForExitAsync(deadline.Token);
				await errors;
				var evidenceFile = JsonSerializer.Deserialize<SemanticRecoveryEvidence>(File.ReadAllText(Path.Join(evidence, "evidence.json")))!;
				return new KilledWorld(evidenceFile, evidence);
			}
			throw new InvalidOperationException($"The recovery child ended before {phase}: {output}");
		}
		finally
		{
			if (!process.HasExited) process.Kill();
		}
	}

	private SemanticWikiPrototypeHost Adopt(KilledWorld killed)
	{
		Environment.SetEnvironmentVariable("NATS_URL", $"nats://localhost:{NatsTestServer.Instance.GetMappedPublicPort(4222)}");
		return SemanticWikiPrototypeHost.AdoptAfterOwnerExit(killed.Evidence.World, MySqlTestServer.Instance.GetConnectionString(), ConfigFile);
	}

	/// <summary>Every page's canonical facts are <paramref name="levels"/>: a level, or none when absent.</summary>
	private static async Task AssertCanonicalFacts(LightningDatabase provider, IEnumerable<(string Id, long? Level)> levels)
	{
		foreach (var (id, level) in levels)
		{
			var facts = ObserveProjection(provider, id).Facts.Facts;
			await Assert.That(facts.Select(f => (f.Property, f.Value)).ToArray())
				.IsEquivalentTo(level is { } value ? [("pf2e:level", (SemanticValue)value)] : Array.Empty<(string, SemanticValue)>());
		}
	}

	/// <summary>
	/// The indexes are verified against the canonical facts: Forward and Postings hold exactly the rows the live
	/// summaries' facts imply, and every class's reference count is the number of live summaries in it.
	/// </summary>
	private static async Task AssertIndexesMatchFacts(LightningDatabase provider)
	{
		var tables = AtomicWikiLayout.ForGeneration(provider.ActiveSemanticGeneration());
		var (expectedForward, expectedPostings, expectedReferences, forward, postings, references) = provider.Store.Read(tx =>
		{
			using var work = WikiSemanticWork.Create(tx, tables, new NativeReadBudget(65536, 64L << 20, 256L << 20, CancellationToken.None));
			var wantForward = new HashSet<string>(StringComparer.Ordinal);
			var wantPostings = new HashSet<string>(StringComparer.Ordinal);
			var wantReferences = new Dictionary<string, long>(StringComparer.Ordinal);
			foreach (var (key, value) in tx.Range(tables.Summaries, []))
			{
				var state = WikiSemanticStateCodec.ProjectionState(value).State;
				var facts = WikiSemanticCodec.DecodeFacts(tx.TryGet(tables.Facts, key, out var bytes) ? bytes : throw new InvalidDataException("A summary has no facts."));
				if (state.Summary.Deleted || facts.Facts.IsEmpty)
				{
					if (state.ClassId != 0 || !facts.Facts.IsEmpty) throw new InvalidDataException("A page without live facts holds a class.");
					continue;
				}
				var pair = Convert.ToHexString(work.Pair(state.CollectionId, state.ClassId));
				wantReferences[pair] = wantReferences.GetValueOrDefault(pair) + 1;
				foreach (var fact in facts.Facts)
				{
					var (f, p) = WikiSemanticProjectionPlanner.IndexKeys(work, state.Summary.NativeId, state.CollectionId, state.ClassId, fact, create: false);
					wantForward.Add(Convert.ToHexString(f));
					wantPostings.Add(Convert.ToHexString(p));
				}
			}
			var haveForward = tx.Range(tables.Forward, []).Select(row => Convert.ToHexString(row.Key)).ToHashSet(StringComparer.Ordinal);
			var havePostings = tx.Range(tables.Postings, []).Select(row => Convert.ToHexString(row.Key)).ToHashSet(StringComparer.Ordinal);
			var haveReferences = tx.Range(tables.Catalog, [])
				.ToDictionary(row => Convert.ToHexString(row.Key), row => WikiSemanticStateCodec.Catalog(row.Value).References, StringComparer.Ordinal);
			return (wantForward, wantPostings, wantReferences, haveForward, havePostings, haveReferences);
		});
		await Assert.That(forward).IsEquivalentTo(expectedForward);
		await Assert.That(postings).IsEquivalentTo(expectedPostings);
		await Assert.That(references).IsEquivalentTo(expectedReferences);
	}

	/// <summary>After the kill: a continuation from before it restarts, and each reader sees exactly the oracle's rows.</summary>
	private async Task AssertReadersAfterRecovery(SemanticWikiPrototypeHost host, SemanticRecoveryEvidence evidence,
		IReadOnlyDictionary<string, long?> levels, bool settled)
	{
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var resumed = await SemanticWikiQueries.RunAsync(provider, _ => ValueTask.FromResult(SemanticRecoveryChild.Admin),
			SemanticRecoveryChild.TokenQuery, evidence.Token, CancellationToken.None);
		await Assert.That(resumed is SemanticQueryResult.Restart).IsTrue();
		var world = new QueryWorld(host, [.. evidence.Pages.Select((p, i) => new QueryPage(p.Id, p.Title, i, levels[p.Id] ?? 0, [], null))], "", "");
		var query = Query(new SemanticPredicate.IntegerRange("pf2e:level", 1, 10));
		foreach (var reader in QueryReaders)
		{
			if (!settled)
			{
				// An unsettled world certifies nothing: the reader gets no rows at all, private or public.
				await Assert.That(await SemanticWikiQueries.RunAsync(provider, _ => ValueTask.FromResult(reader), query, null, CancellationToken.None)
					is SemanticQueryResult.NotCertified { Outcome: var outcome } && outcome.GetType().Name == "Incomplete").IsTrue();
				continue;
			}
			var oracle = await OracleAsync(world, reader, p => p.Level is >= 1 and <= 10, byLevel: true);
			await Assert.That((await PageThroughAsync(world, reader, query)).Ids).IsEquivalentTo(oracle, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		}
	}

	private static Dictionary<string, long?> SeededLevels(SemanticRecoveryEvidence evidence)
		=> evidence.Pages.ToDictionary(p => p.Id, p => (long?)p.Level);

	private static void RemoveEvidence(KilledWorld killed)
	{
		foreach (var name in new[] { "spec.json", "evidence.json" })
		{
			var path = Path.Join(killed.EvidencePath, name);
			if (File.Exists(path)) File.Delete(path);
		}
		Directory.Delete(killed.EvidencePath);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task AKillAroundAPageCommitLeavesTheLastDurableFactsAndMatchingIndexes(bool committed)
	{
		var killed = await KillChildAtAsync(committed ? "post-commit" : "page-commit", committed ? "committed" : "applied-uncommitted");
		try
		{
			await using var host = Adopt(killed);
			await host.OpenAsync();
			var provider = host.Services.GetRequiredService<LightningDatabase>();
			var wiki = host.Services.GetRequiredService<IWikiService>();
			var levels = SeededLevels(killed.Evidence);
			// A committed edit dropped the page's facts in its own job; an uncommitted one changed nothing.
			if (committed) levels[killed.Evidence.Victim] = null;
			await AssertCanonicalFacts(provider, levels.Select(pair => (pair.Key, pair.Value)));
			await Assert.That((await wiki.GetByIdAsync(killed.Evidence.Victim)).Value is WikiPage { MarkdownSource: var body }
				&& body.StartsWith(committed ? "# Edited" : "# Recovery 0", StringComparison.Ordinal)).IsTrue();
			await AssertIndexesMatchFacts(provider);
			await Assert.That(Settled(host)).IsTrue();
			await AssertReadersAfterRecovery(host, killed.Evidence, levels, settled: true);
			await host.CloseAsync();
		}
		finally { RemoveEvidence(killed); }
	}

	[Test]
	[Arguments("package-job", "package-written")]
	[Arguments("package-compensation", "compensating")]
	public async Task AKillInsideAPackageOperationLeavesTheWorldUnknownAndItsIndexesConsistent(string scenario, string phase)
	{
		var killed = await KillChildAtAsync(scenario, phase);
		try
		{
			await using var host = Adopt(killed);
			await host.OpenAsync();
			var provider = host.Services.GetRequiredService<LightningDatabase>();
			var levels = SeededLevels(killed.Evidence);
			await AssertCanonicalFacts(provider, levels.Select(pair => (pair.Key, pair.Value)));
			await AssertIndexesMatchFacts(provider);
			// The operation never settled, so the world is UNKNOWN until a coordinated operation settles it.
			await Assert.That(PackageState(provider)[5]).IsEqualTo((byte)0);
			await Assert.That(await ProbeReadAsync(host) is SemanticReadResult<string>.Incomplete).IsTrue();
			await AssertReadersAfterRecovery(host, killed.Evidence, levels, settled: false);
			if (scenario == "package-job")
			{
				var wiki = host.Services.GetRequiredService<IWikiService>();
				var road = (await wiki.GetBySlugAsync("recovery_road", WikiNamespace.Main)).Value as WikiPage;
				await Assert.That(road is not null && ObserveProjection(provider, road.Id).State.Source is PortableWikiSource).IsTrue();
			}
			await host.CloseAsync();
		}
		finally { RemoveEvidence(killed); }
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	public async Task TheIndexVerifierRejectsAMissingAStaleOrAMiscountedRow(int damage)
	{
		await using var host = await ActiveHostAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var page = (await host.Services.GetRequiredService<IWikiService>().CreateAsync("Verifier target", "# Target", "#1")).Value as WikiPage
			?? throw new InvalidOperationException("Could not create the page.");
		await provider.ImportSemanticFixtureFactsAsync([new(page.Id, new(AtomicWikiLayout.Schema.Version,
			[new(new(page.Id), "pf2e:level", 3L, QueryEvidence)]))], CancellationToken.None);
		await AssertIndexesMatchFacts(provider);
		await provider.Store.WriteAsync(tx =>
		{
			switch (damage)
			{
				case 0: tx.Delete(AtomicWikiLayout.Forward, tx.Range(AtomicWikiLayout.Forward, []).First().Key); break;
				case 1: tx.Put(AtomicWikiLayout.Postings, [.. tx.Range(AtomicWikiLayout.Postings, []).First().Key[..^1], 0xFF], []); break;
				default:
					var (key, value) = tx.Range(AtomicWikiLayout.Catalog, []).First();
					var entry = WikiSemanticStateCodec.Catalog(value);
					tx.Put(AtomicWikiLayout.Catalog, key, WikiSemanticStateCodec.Catalog(entry with { References = entry.References + 1 }));
					break;
			}
		});
		await Assert.That(async () => await AssertIndexesMatchFacts(provider)).Throws<TUnit.Assertions.Exceptions.AssertionException>();
		await host.CloseAsync();
	}

	[Test]
	[Arguments("rebuild-batch", false)]
	[Arguments("rebuild-checkpoint", false)]
	[Arguments("rebuild-flip", false)]
	[Arguments("rebuild-flipped", true)]
	public async Task AKillDuringARebuildLeavesOneConsistentActiveGeneration(string scenario, bool flipped)
	{
		var killed = await KillChildAtAsync(scenario, scenario);
		try
		{
			await using var host = Adopt(killed);
			await host.OpenAsync();
			var provider = host.Services.GetRequiredService<LightningDatabase>();
			var levels = SeededLevels(killed.Evidence);
			// Before the flip committed, the old generation is still the one; after it, the staged one is. Either
			// way reopening discarded the staging, and the active generation holds exactly what the facts imply.
			await Assert.That(provider.ActiveSemanticGeneration()).IsEqualTo(flipped ? 2UL : 1UL);
			await AssertNoStaging(provider);
			if (!flipped) await AssertGenerationCleared(provider, 2);
			await AssertCanonicalFacts(provider, levels.Select(pair => (pair.Key, pair.Value)));
			await AssertIndexesMatchFacts(provider);
			await AssertReadersAfterRecovery(host, killed.Evidence, levels, settled: true);
			// The next rebuild clears whatever an interrupted retirement left in its slot and publishes cleanly.
			await Assert.That(await provider.RebuildSemanticProjectionAsync(null, CancellationToken.None)
				is SemanticRebuildOutcome.Flipped { Generation: var next } && next == (flipped ? 3UL : 2UL)).IsTrue();
			await AssertGenerationCleared(provider, flipped ? 2UL : 1UL);
			await AssertIndexesMatchFacts(provider);
			await host.CloseAsync();
		}
		finally { RemoveEvidence(killed); }
	}

	[Test]
	public async Task AKillInsideAReadRequirementRewriteLeavesEveryPageInItsOldClass()
	{
		var killed = await KillChildAtAsync("rewrite", "rewrite");
		try
		{
			await using var host = Adopt(killed);
			await host.OpenAsync();
			var provider = host.Services.GetRequiredService<LightningDatabase>();
			var wiki = host.Services.GetRequiredService<IWikiService>();
			// The rule and every page it moved shared one job, which never committed: no page moved without the rule.
			await Assert.That((await provider.GetRequirementsAsync()).Any(set => set.Target == WikiRuleTarget.ForNamespace("main"))).IsFalse();
			foreach (var page in killed.Evidence.Pages)
				await Assert.That(ObserveProjection(provider, page.Id).State.State.Summary.Visibility.RuleIds).DoesNotContain(WikiRuleTarget.ForNamespace("main").ToString());
			var levels = SeededLevels(killed.Evidence);
			await AssertCanonicalFacts(provider, levels.Select(pair => (pair.Key, pair.Value)));
			await AssertIndexesMatchFacts(provider);
			await AssertReadersAfterRecovery(host, killed.Evidence, levels, settled: true);
			await host.CloseAsync();
		}
		finally { RemoveEvidence(killed); }
	}
}
