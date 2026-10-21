using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

/// <summary>What a recovery test hands the child process it will kill.</summary>
internal sealed record SemanticRecoverySpec(string Scenario, string ScratchRoot, string MySql, string Nats, string Config, string Evidence);

/// <summary>What the child seeded before its kill point, for the parent to check after it reopens the world.</summary>
internal sealed record SemanticRecoveryEvidence(string World, Guid FixtureId, string Victim, string Token,
	IReadOnlyList<SemanticRecoveryPage> Pages);

internal sealed record SemanticRecoveryPage(string Id, string Title, long Level, bool Published);

/// <summary>Skipped unless a recovery test launched this process as its child.</summary>
internal sealed class RecoveryChildOnlyAttribute() : SkipAttribute("Runs only as a recovery test's child process.")
{
	public override Task<bool> ShouldSkip(TestRegisteredContext context)
		=> Task.FromResult(Environment.GetEnvironmentVariable(SemanticRecoveryChild.Variable) is null);
}

/// <summary>
/// The child side of the kill-point tests: opens a real issued world, seeds it, then blocks at one named point,
/// where the parent kills this process with SIGKILL. Nothing here cleans up; the parent adopts the world.
/// </summary>
public class SemanticRecoveryChild
{
	internal const string Variable = "SHARPMUSH_SEMANTIC_RECOVERY";
	internal const string Prefix = "RECOVERY ";
	internal static readonly FactProvenance Evidence = new("recovery", "commit", "path", "field", false);
	internal static readonly SemanticWikiQuery TokenQuery = new("game", [new SemanticPredicate.IntegerRange("pf2e:level", 1, 10)], ["pf2e:level"], 2);
	internal static readonly WikiReader Admin = WikiReader.From(["wiki.read", "wiki.admin", "wiki.drafts"], "#1");

	private static readonly Stream Stdout = Console.OpenStandardOutput();
	private static SemanticRecoverySpec? _spec;
	private static string _world = "";

	/// <summary>Writes straight to the process's stdout, past the test framework's capture, and flushes.</summary>
	private static void Checkpoint(string phase)
	{
		var line = Prefix + JsonSerializer.Serialize(new { pid = Environment.ProcessId, scenario = _spec!.Scenario, world = _world, phase }) + "\n";
		lock (Stdout) { Stdout.Write(System.Text.Encoding.UTF8.GetBytes(line)); Stdout.Flush(); }
	}

	private static void Hold(string phase)
	{
		Checkpoint(phase);
		Thread.Sleep(Timeout.Infinite);
	}

	[Test]
	[RecoveryChildOnly]
	public async Task Run()
	{
		_spec = JsonSerializer.Deserialize<SemanticRecoverySpec>(File.ReadAllText(Environment.GetEnvironmentVariable(Variable)!))!;
		Environment.SetEnvironmentVariable("NATS_URL", _spec.Nats);
		var host = new SemanticWikiPrototypeHost(_spec.ScratchRoot, _spec.MySql, _spec.Config, null, "mysql", null);
		_world = host.WorldPath;
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var pages = new List<SemanticRecoveryPage>();
		for (var i = 0; i < 4; i++)
		{
			var page = (await wiki.CreateAsync($"Recovery {i}", $"# Recovery {i}", "#1", WikiNamespace.Main, null, ["Spells"])).Value as WikiPage
				?? throw new InvalidOperationException("Could not seed a page.");
			// The last page is a draft only drafts readers may see.
			if (i == 3) await wiki.SetMetadataAsync(page.Id, ["Spells"], published: false);
			pages.Add(new(page.Id, page.Title, i + 1, i != 3));
		}
		await provider.ImportSemanticFixtureFactsAsync([.. pages.Select(p => new SemanticFixtureFactImport(p.Id,
			new(AtomicWikiLayout.Schema.Version, [new(new(p.Id), "pf2e:level", p.Level, Evidence)])))], CancellationToken.None);
		var first = await SemanticWikiQueries.RunAsync(provider, _ => ValueTask.FromResult(Admin), TokenQuery, null, CancellationToken.None);
		var token = (first as SemanticQueryResult.Page)?.Continuation ?? throw new InvalidOperationException("Expected a continuation.");
		File.WriteAllText(Path.Combine(_spec.Evidence, "evidence.json"),
			JsonSerializer.Serialize(new SemanticRecoveryEvidence(host.WorldPath, host.FixtureId, pages[0].Id, token, pages)));
		Checkpoint("seeded");

		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		var runner = host.Services.GetRequiredService<IPackageOperationRunner>();
		switch (_spec.Scenario)
		{
			case "page-commit":
				var held = 0;
				provider.AtomicLegacyPutApplied = _ => { if (Interlocked.Exchange(ref held, 1) == 0) Hold("applied-uncommitted"); };
				await wiki.UpdateAsync(pages[0].Id, "# Edited", "#1");
				break;
			case "post-commit":
				provider.AtomicLegacyCommitted = _ => Hold("committed");
				await wiki.UpdateAsync(pages[0].Id, "# Edited", "#1");
				break;
			case "package-job":
				await runner.RunAsync("recovery-package", async _ =>
				{
					var applied = await installer.ApplyAsync(SemanticRecoveryScenarios.Package(), SemanticRecoveryScenarios.Request());
					if (applied.Value is not PackageApplyResult) throw new InvalidOperationException("The package did not apply.");
					Hold("package-written");
					return 0;
				});
				break;
			case "package-compensation":
				var factory = host.Services.GetRequiredService<IPackageWriteTransactionFactory>();
				await runner.RunAsync("recovery-compensation", async _ =>
				{
					var writes = await factory.CreateAsync(new DBRef(7));
					var created = (await wiki.CreateAsync("Recovery compensated", "# Compensated", "#1")).Value as WikiPage
						?? throw new InvalidOperationException("Could not create the compensated page.");
					writes.Track("recovery compensated page", async () => await wiki.DeleteAsync(created.Id, "#1"));
					writes.Track("recovery kill point", () => { Hold("compensating"); return Task.CompletedTask; });
					await writes.DisposeAsync();
					return 0;
				});
				break;
			default: throw new InvalidOperationException("Unknown recovery scenario.");
		}
		Checkpoint("ended-without-kill");
	}
}

/// <summary>The package the package kill points install.</summary>
internal static class SemanticRecoveryScenarios
{
	internal static PackageManifest Package() => new PackageManifestService().ParseManifest("""
		format: 1.4
		package: recovery-pack
		version: "1.0.0"
		wiki:
		  - page: main:recovery_road
		    title: Recovery Road
		    markdown: The road runs north.
		""") switch
	{
		ParsedPackageManifest parsed => parsed.Manifest,
		PackageManifestFailure failure => throw new InvalidOperationException(string.Join("; ", failure.Issues))
	};

	internal static PackageApplyRequest Request() => new(new("https://example.invalid/recovery", null, "recovery-commit", null),
		new Dictionary<string, string>(), []);
}
