using System.Collections.Concurrent;
using System.Security.Cryptography;
using LightningDB;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Plugins.Storage;
using LightningDatabase = SharpMUSH.Database.Lightning.LightningDatabase;

namespace SharpMUSH.Tests.Wiki.Semantics;

public partial class IntegratedWikiMutationTests
{
	[Test]
	public async Task ReopenStartupUsesActiveModeBeforeFirstMigrationWrite()
	{
		await using var host = NewHost();
		var observed = new ConcurrentQueue<SemanticFixturePhase>();
		var opened = new ConcurrentQueue<SemanticFixturePhase>();
		host.ProviderOpened = provider =>
		{
			opened.Enqueue(provider.AtomicWikiPhase);
			provider.AtomicMigrationGuardObserved = phase => observed.Enqueue(phase);
		};
		await host.OpenAsync();
		await Assert.That(opened.ToArray()).IsEquivalentTo(new[] { SemanticFixturePhase.Inactive });
		await Assert.That(observed.ToArray()).IsEquivalentTo(new[] { SemanticFixturePhase.Inactive });
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var before = WikiFingerprint(provider, true);
		await host.CloseAsync();
		await host.OpenAsync();
		await Assert.That(opened.ToArray()).IsEquivalentTo(new[] { SemanticFixturePhase.Inactive, SemanticFixturePhase.Active });
		await Assert.That(observed.ToArray()).IsEquivalentTo(new[] { SemanticFixturePhase.Inactive, SemanticFixturePhase.Active });
		await Assert.That(WikiFingerprint(host.Services.GetRequiredService<LightningDatabase>(), true)).IsEqualTo(before);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task MissingActiveTableOrExtraReservedLayoutRefusesReadOnlyBeforeConstructor(bool extra)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		await host.CloseAsync();
		// Fully closed, exclusively owned disposable world. Use valid named-database operations only.
		using (var environment = new LightningEnvironment(host.WorldPath, new EnvironmentConfiguration { MaxDatabases = 256 }))
		{
			environment.Open(EnvironmentOpenFlags.None);
			using var transaction = environment.BeginTransaction();
			using var table = transaction.OpenDatabase(extra ? AtomicWikiLayout.ReservedPrefix + "unexpected" : AtomicWikiLayout.Facts.Name,
				new DatabaseConfiguration { Flags = extra ? DatabaseOpenFlags.Create : DatabaseOpenFlags.None });
			if (!extra) transaction.DropDatabase(table);
			var result = transaction.Commit();
			if (result != MDBResultCode.Success) throw new InvalidOperationException("Owned layout setup did not commit.");
		}
		var before = SHA256.HashData(File.ReadAllBytes(Path.Join(host.WorldPath, "data.mdb")));
		var published = false;
		host.ProviderOpened = _ => published = true;
		await Assert.That(() => host.OpenAsync()).Throws<InvalidOperationException>();
		await Assert.That(published).IsFalse();
		await Assert.That(SHA256.HashData(File.ReadAllBytes(Path.Join(host.WorldPath, "data.mdb")))).IsEquivalentTo(before);
	}

	[Test]
	public async Task InactiveOwnershipCascadeAndHistoryHoldCountedLeaseThroughActualParent()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = (await wiki.CreateAsync("Owner and history leases", "first", "#1", sourceLocale: "en")).Expect<WikiPage>();
		await wiki.UpdateAsync(page.Id, "second", "#1");
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var owner = new PackageWikiPageRecord("fixture-owner", page.Id, page.Namespace, page.Slug, page.Title,
			page.MarkdownSource, page.Categories, page.Published, PackageWikiRequires.None, false, "1.0.0");
		var rule = new HistoryRetentionRule { KeepNewest = 1 };
		var batch = await provider.WikiHistory.FindPurgeableAsync(rule, DateTimeOffset.UtcNow, [], 128);
		foreach (var operation in new Func<Task>[]
		{
			() => provider.UpsertPackageWikiPageAsync(owner),
			() => provider.RemovePackageWikiPageAsync(owner.PackageId, owner.Namespace, owner.Slug),
			() => provider.RemoveInstalledPackageAsync(owner.PackageId),
			async () => { await provider.WikiHistory.PurgeAsync(batch, rule, DateTimeOffset.UtcNow); }
		})
		{
			provider.Store.PauseWriter();
			Task? parent = null;
			try
			{
				parent = operation();
				await WaitForLease(provider);
				await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
				using var cancellation = new CancellationTokenSource();
				var waiter = parent.WaitAsync(cancellation.Token);
				cancellation.Cancel();
				await Assert.That(() => waiter).Throws<OperationCanceledException>();
				await Assert.That(provider.AtomicWikiLegacyMutationsInFlight).IsEqualTo(1);
			}
			finally { provider.Store.ResumeWriter(); if (parent is not null) await parent; }
			await Assert.That(provider.AtomicWikiLegacyMutationsInFlight).IsEqualTo(0);
		}
		await Assert.That((await wiki.GetRevisionsAsync(page.Id)).Count).IsEqualTo(1);
		await host.ActivateAtomicWikiProjectionAsync();
	}

	[Test]
	public async Task DirectAndRunnerAdmissionRaceHasNoOwnerGateInversion()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var gate = host.Services.GetRequiredService<IPackageOperationGate>();
		var release = NewSignal();
		var started = NewSignal();
		var held = gate.RunAsync(async () => { started.TrySetResult(); await release.Task; return 0; });
		await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		var direct = installer.UninstallAsync("fixture-race-missing");
		var runner = host.Services.GetRequiredService<IPackageOperationRunner>().RunAsync("fixture-direct-race",
			async token => await installer.UninstallAsync("fixture-nested-missing", cancellationToken: token));
		try
		{
			await Assert.That(direct.IsCompleted || runner.IsCompleted).IsFalse();
			await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
		}
		finally { release.TrySetResult(); await held; await direct; await runner; }
		await Assert.That(await direct is Error<string>).IsTrue();
		await Assert.That((await runner).Expect<PackageOperationRan<Result<Success>>>().Result is Error<string>).IsTrue();
		await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
	}

	[Test]
	public async Task ConcurrentSameNameRawTableOpenAdvancesOnlyOneCreationEpoch()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var accessor = host.Services.GetRequiredService<ILightningStorageAccessor>();
		var before = ObserveRawEpoch(provider);
		var ready = NewSignal();
		var arrivals = 0;
		provider.Store.PauseWriter();
		var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
		{
			if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
			return accessor.OpenTable("fixture.same-name.concurrent", false);
		})).ToArray();
		try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
		finally { provider.Store.ResumeWriter(); await Task.WhenAll(tasks); }
		await Assert.That(tasks[0].Result.Name).IsEqualTo(tasks[1].Result.Name);
		await Assert.That(ObserveRawEpoch(provider)).IsEqualTo(before + 1);
		await host.CloseAsync();
		await host.OpenAsync();
		await Assert.That(ObserveRawEpoch(host.Services.GetRequiredService<LightningDatabase>())).IsEqualTo(before + 1);
	}
}
