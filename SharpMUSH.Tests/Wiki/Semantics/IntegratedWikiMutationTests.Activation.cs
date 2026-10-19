using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

public partial class IntegratedWikiMutationTests
{
	[Test]
	public async Task ActivationBootstrapsActualHomeAndMarkdownGuideAndNamedCategory()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var category = (await wiki.NameCategoriesAsync(["Places of Note"], [], "#1", "en")).Single();
		var pages = new[] { (await wiki.GetBySlugAsync("home")).Expect<WikiPage>(),
			(await wiki.GetBySlugAsync("markdown_guide", WikiNamespace.Help)).Expect<WikiPage>(), category };
		var histories = await Task.WhenAll(pages.Select(page => wiki.GetRevisionsAsync(page.Id)));
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var states = pages.Select(page => ObserveProjection(provider, page.Id)).ToArray();
		await Assert.That(states.Select(state => state.State.State.Summary.NativeId).Distinct().Count()).IsEqualTo(3);
		var certificate = ObserveActivation(provider, host.FixtureId);
		await Assert.That(certificate.Active).IsTrue();
		await Assert.That(certificate.Count).IsEqualTo(3);
		await Assert.That(ObserveSequence(provider)).IsEqualTo(certificate.Sequence);
		await Assert.That(ObserveMutationCount(provider)).IsEqualTo(0L);
		for (var i = 0; i < pages.Length; i++)
		{
			await Assert.That(states[i].State.Source is LocalWikiSource).IsTrue();
			await Assert.That(states[i].State.State.Summary.Reference.Id).IsEqualTo(pages[i].Id);
			await Assert.That(states[i].State.State.Summary.Title).IsEqualTo(pages[i].Title);
			await Assert.That(states[i].State.State.Summary.Collection).IsEqualTo("game");
			await Assert.That(states[i].State.State.Summary.StateVersion).IsEqualTo(1L);
			await Assert.That(states[i].State.State.CommitSequence).IsEqualTo(certificate.Sequence);
			await Assert.That(states[i].Facts.Facts.Length).IsEqualTo(0);
			await AssertPageMatches((await wiki.GetByIdAsync(pages[i].Id)).Expect<WikiPage>(), pages[i]);
			await Assert.That(await wiki.GetRevisionsAsync(pages[i].Id)).IsEquivalentTo(histories[i]);
		}
		await host.CloseAsync();
		await AssertFixtureProjectionLayout(host.WorldPath);
		await host.OpenAsync();
		var reopened = host.Services.GetRequiredService<LightningDatabase>();
		await Assert.That(reopened.AtomicWikiPhase).IsEqualTo(SemanticFixturePhase.Active);
		await Assert.That(ObserveActivation(reopened, host.FixtureId)).IsEqualTo(certificate);
		for (var i = 0; i < pages.Length; i++)
		{
			await Assert.That(ObserveProjection(reopened, pages[i].Id).SummaryBytes).IsEquivalentTo(states[i].SummaryBytes);
			await Assert.That(await host.Services.GetRequiredService<IWikiService>().GetRevisionsAsync(pages[i].Id)).IsEquivalentTo(histories[i]);
		}
	}

	[Test]
	public async Task ActivationRequiresInstalledPackageAdmission()
	{
		await using var host = NewHost(services =>
		{
			services.RemoveAll<IPackageOperationRunner>();
			services.AddSingleton<IPackageOperationRunner, PackageOperationRunner>();
		});
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var before = WikiFingerprint(provider, false);
		await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
		await Assert.That(WikiFingerprint(provider, false)).IsEqualTo(before);
		await Assert.That(provider.AtomicWikiPhase).IsEqualTo(SemanticFixturePhase.Inactive);
		await host.CloseAsync();
		await AssertNoSemanticTables(host.WorldPath);
	}

	[Test]
	public async Task PreflightQuotaRejectThenLegacyWritesStillWork()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = (await wiki.CreateAsync("Oversized inactive page", new string('x', 160 * 1024), "#1", sourceLocale: "en")).Expect<WikiPage>();
		await Assert.That(page.MarkdownSource.Length).IsEqualTo(160 * 1024);
		await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.WorkLimit);
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		await Assert.That(provider.AtomicWikiPhase).IsEqualTo(SemanticFixturePhase.Inactive);
		var edited = (await wiki.UpdateAsync(page.Id, "Small committed body", "#1")).Expect<WikiPage>();
		await Assert.That(edited.RevisionNumber).IsEqualTo(2);
		await wiki.SetRequirementsAsync(WikiRuleTarget.ForCategory("legacy"), WikiRequirementSet.Protection, "#1");
		var accessor = host.Services.GetRequiredService<ILightningStorageAccessor>();
		var table = accessor.OpenTable("fixture.plugin.preflight", false);
		await accessor.WriteAsync(tx => { tx.Put(table, [1], [2]); return true; });
		await host.CloseAsync();
		await AssertNoSemanticTables(host.WorldPath);
		await host.OpenAsync();
		await Assert.That((await host.Services.GetRequiredService<IWikiService>().GetByIdAsync(page.Id)).Expect<WikiPage>().MarkdownSource).IsEqualTo("Small committed body");
		await host.ActivateAtomicWikiProjectionAsync();
	}

	[Test]
	public async Task InactiveWriterLeaseSurvivesCancelledWaiterAndBootstrapsCommittedBody()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = (await wiki.CreateAsync("Paused inactive write", "before", "#1", sourceLocale: "en")).Expect<WikiPage>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		provider.Store.PauseWriter();
		Task<Found<WikiPage>>? parent = null;
		try
		{
			parent = wiki.UpdateAsync(page.Id, "committed-after-pause", "#1");
			await WaitForLease(provider);
			await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
			using var cancellation = new CancellationTokenSource();
			var waiter = parent.WaitAsync(cancellation.Token);
			cancellation.Cancel();
			await Assert.That(() => waiter).Throws<OperationCanceledException>();
			await Assert.That(parent.IsCompleted).IsFalse();
			await Assert.That(provider.AtomicWikiLegacyMutationsInFlight).IsEqualTo(1);
			await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
		}
		finally
		{
			provider.Store.ResumeWriter();
			if (parent is not null) await parent;
		}
		await Assert.That(provider.AtomicWikiLegacyMutationsInFlight).IsEqualTo(0);
		await host.ActivateAtomicWikiProjectionAsync();
		await Assert.That(ObserveProjection(provider, page.Id).State.State.Summary.Reference.Id).IsEqualTo(page.Id);
		await Assert.That((await wiki.GetByIdAsync(page.Id)).Expect<WikiPage>().MarkdownSource).IsEqualTo("committed-after-pause");
	}

	[Test]
	public async Task ConcurrentCloseActivateRefusesAndActivatingMutationsRefuse()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		provider.AtomicActivationHook = stage =>
		{
			if (stage != AtomicWikiActivationStage.PreflightComplete) return Task.CompletedTask;
			reached.TrySetResult();
			return release.Task;
		};
		var activation = host.ActivateAtomicWikiProjectionAsync();
		try
		{
			await reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
			await Assert.That(() => host.CloseAsync().AsTask()).Throws<InvalidOperationException>();
			await Assert.That(() => host.OpenAsync()).Throws<InvalidOperationException>();
			await Assert.That(() => host.DisposeAsync().AsTask()).Throws<InvalidOperationException>();
			await AssertFixtureFailure(async () => { await host.Services.GetRequiredService<IWikiService>().CreateAsync("racing", "body", "#1"); }, WikiFixtureMutationFailure.Unavailable);
			await AssertFixtureFailure(() => provider.WriteAsync(tx => true).AsTask(), WikiFixtureMutationFailure.Unavailable);
			await AssertFixtureFailure(() => { provider.OpenTable("racing.plugin", false); return Task.CompletedTask; }, WikiFixtureMutationFailure.Unavailable);
			await Assert.That(await host.Services.GetRequiredService<IPackageInstallService>().UninstallAsync("racing-package") is Error<string>).IsTrue();
			await Assert.That(await host.Services.GetRequiredService<IPackageOperationRunner>().RunAsync<int>("racing-package",
				_ => throw new ActivationFault()) is PackageOperationRefused).IsTrue();
		}
		finally { release.TrySetResult(); await activation; }
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	public async Task IncompleteMarkerFaultMakesEveryMutationUnavailableAndReopenRefuses(int afterTable)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var faultStage = afterTable switch { 0 => AtomicWikiActivationStage.IncompleteCommitted, 1 => AtomicWikiActivationStage.FirstTableOpened, _ => AtomicWikiActivationStage.LayoutMutationStarting };
		provider.AtomicActivationHook = stage => stage == faultStage
			? throw new ActivationFault() : Task.CompletedTask;
		await Assert.That(() => host.ActivateAtomicWikiProjectionAsync()).Throws<ActivationFault>();
		await Assert.That(provider.AtomicWikiPhase).IsEqualTo(SemanticFixturePhase.Failed);
		await AssertFixtureFailure(async () => { await host.Services.GetRequiredService<IWikiService>().CreateAsync("failed", "body", "#1"); }, WikiFixtureMutationFailure.Unavailable);
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var home = (await wiki.GetBySlugAsync("home")).Expect<WikiPage>();
		var unchanged = WikiFingerprint(provider, false);
		var owner = new PackageWikiPageRecord("failed-owner", home.Id, home.Namespace, home.Slug, home.Title,
			home.MarkdownSource, home.Categories, home.Published, PackageWikiRequires.None, false, "1.0.0");
		foreach (var operation in new Func<Task>[]
		{
			async () => { await wiki.UpdateAsync(home.Id, "failed body", "#1"); },
			async () => { await wiki.SetTitleAsync(home.Id, "failed title"); },
			async () => { await wiki.SetMetadataAsync(home.Id, ["failed"], false); },
			async () => { await wiki.DeleteAsync(home.Id, "#1"); },
			async () => { await wiki.SetRequirementsAsync(WikiRuleTarget.ForPage(home.Id), WikiRequirementSet.Protection, "#1"); },
			async () => { await wiki.SetCategoryPinnedAsync("failed", true); },
			async () => { await wiki.UpsertTranslationAsync(home.Id, "fr", "failed", "failed", "#1", null, true, null); },
			async () => { await wiki.DeleteTranslationAsync(home.Id, "fr", "#1"); },
			() => provider.UpsertPackageWikiPageAsync(owner),
			() => provider.RemovePackageWikiPageAsync(owner.PackageId, owner.Namespace, owner.Slug),
			() => provider.RemoveInstalledPackageAsync(owner.PackageId),
			async () => { await provider.WikiHistory.PurgeAsync(new HistoryPurgeBatch([], []), new HistoryRetentionRule { KeepNewest = 1 }, DateTimeOffset.UtcNow); }
		})
		{
			await AssertFixtureFailure(operation, WikiFixtureMutationFailure.Unavailable);
			await Assert.That(WikiFingerprint(provider, false)).IsEqualTo(unchanged);
		}
		await AssertFixtureFailure(() => provider.WriteAsync(tx => true).AsTask(), WikiFixtureMutationFailure.Unavailable);
		await AssertFixtureFailure(() => { provider.OpenTable("failed.plugin", false); return Task.CompletedTask; }, WikiFixtureMutationFailure.Unavailable);
		await host.CloseAsync();
		await Assert.That(() => host.OpenAsync()).Throws<InvalidOperationException>();
	}

	[Test]
	public async Task PartialTableLayoutReopenRefusesBeforeConstructor()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		// Valid owned inactive raw setup, then fully closed corroboration; no malformed native data.
		host.Services.GetRequiredService<ILightningStorageAccessor>().OpenTable(AtomicWikiLayout.Metadata.Name, false);
		await host.CloseAsync();
		var before = SHA256.HashData(File.ReadAllBytes(Path.Combine(host.WorldPath, "data.mdb")));
		await Assert.That(() => host.OpenAsync()).Throws<InvalidOperationException>();
		await Assert.That(SHA256.HashData(File.ReadAllBytes(Path.Combine(host.WorldPath, "data.mdb")))).IsEquivalentTo(before);
	}

	[Test]
	public async Task ActiveRawGuardPoisonsCaughtViolationsAndKeepsEpochAtomicAcrossReopen()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var accessor = host.Services.GetRequiredService<ILightningStorageAccessor>();
		var plugin = accessor.OpenTable("fixture.plugin.atomic", false);
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		foreach (var protectedAlias in new[] { TableDef.Node(Tables.WikiPage.Name), TableDef.Index(Tables.Meta.Name, duplicates: true),
			TableDef.Node(AtomicWikiLayout.Summaries.Name), TableDef.Node("prototype.semantic.unknown") })
		{
			foreach (var operation in new Action<ITx>[]
			{
			tx => tx.Put(protectedAlias, [1], [2]),
			tx => { tx.Delete(protectedAlias, [1]); },
			tx => { tx.Delete(protectedAlias, [1], [2]); },
			tx => { tx.DeletePrefix(protectedAlias, []); }
			})
			{
				var before = WikiFingerprint(provider, true);
				await AssertFixtureFailure(() => accessor.WriteAsync(tx =>
				{
					tx.Put(plugin, [7], [8]);
					try { operation(tx); } catch (WikiFixtureMutationException) { }
					return true;
				}).AsTask(), WikiFixtureMutationFailure.UnsupportedOperation);
				await Assert.That(accessor.Read(tx => tx.TryGet(plugin, new byte[] { 7 }, out _))).IsFalse();
				await Assert.That(WikiFingerprint(provider, true)).IsEqualTo(before);
			}
		}
		await AssertFixtureFailure(() => { accessor.OpenTable(Tables.WikiPage.Name, false); return Task.CompletedTask; }, WikiFixtureMutationFailure.UnsupportedOperation);
		await AssertFixtureFailure(() => { accessor.OpenTable("prototype.semantic.b2.v1.unexpected", false); return Task.CompletedTask; }, WikiFixtureMutationFailure.UnsupportedOperation);
		await accessor.WriteAsync(tx => { tx.Put(plugin, [7], [8]); return true; });
		await Assert.That(ObserveRawEpoch(provider)).IsEqualTo(1UL);
		var created = accessor.OpenTable("fixture.plugin.second", false);
		await Assert.That(ObserveRawEpoch(provider)).IsEqualTo(2UL);
		accessor.OpenTable(created.Name, false);
		await Assert.That(ObserveRawEpoch(provider)).IsEqualTo(2UL);
		await host.CloseAsync();
		await host.OpenAsync();
		var reopened = host.Services.GetRequiredService<ILightningStorageAccessor>();
		await Assert.That(ObserveRawEpoch(host.Services.GetRequiredService<LightningDatabase>())).IsEqualTo(2UL);
		var retainedPlugin = reopened.OpenTable(plugin.Name, false);
		await Assert.That(reopened.Read(tx => tx.TryGet(retainedPlugin, new byte[] { 7 }, out var value) && value.SequenceEqual(new byte[] { 8 }))).IsTrue();
		await Assert.That(ObserveRawEpoch(host.Services.GetRequiredService<LightningDatabase>())).IsEqualTo(2UL);
	}

	[Test]
	public async Task ActivePackageRunnerRefusesBeforeBodyAndProvenance()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var invoked = false;
		var outcome = await host.Services.GetRequiredService<IPackageOperationRunner>().RunAsync("fixture-test",
			_ => { invoked = true; return Task.FromResult(1); }, provenance: _ => { invoked = true; throw new ActivationFault(); });
		await Assert.That(outcome is PackageOperationRefused).IsTrue();
		await Assert.That(invoked).IsFalse();
	}

	[Test]
	public async Task EarlierFailedDirectPackageAdmissionPermanentlyPreventsActivation()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		await installer.GetReadmeAsync("missing-fixture-package");
		await installer.UninstallAsync("missing-fixture-package");
		await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
		await host.CloseAsync();
		await AssertNoSemanticTables(host.WorldPath);
	}

	private sealed class ActivationFault : Exception { }
	private sealed record ProjectionObservation(byte[] SummaryBytes, WikiSemanticProjectionState State, CanonicalFactSet Facts);
	private static ProjectionObservation ObserveProjection(LightningDatabase provider, string id)
		=> provider.Store.Read(tx =>
		{
			using var work = WikiSemanticWork.Create(tx, AtomicWikiLayout.Tables, new NativeReadBudget(128, 128 * 1024, 4 * 1024 * 1024, CancellationToken.None));
			var nativeId = work.Registry(WikiSemanticRegistryKind.Page, id, false);
			if (nativeId == 0) throw new InvalidOperationException("Expected a committed real page projection.");
			var key = WikiSemanticKeys.Subject(nativeId);
			var bytes = work.ReadBytes(AtomicWikiLayout.Summaries, key) ?? throw new InvalidOperationException("Expected projection summary.");
			var facts = work.Read(AtomicWikiLayout.Facts, key, value => WikiSemanticCodec.DecodeFacts(value)) ?? throw new InvalidOperationException("Expected canonical facts.");
			return new ProjectionObservation(bytes, WikiSemanticStateCodec.ProjectionState(bytes), facts);
		});
	private static long ObserveSequence(LightningDatabase provider) => provider.Store.Read(tx =>
	{
		using var work = WikiSemanticWork.Create(tx, AtomicWikiLayout.Tables, new NativeReadBudget(16, 4096, 64 * 1024, CancellationToken.None));
		return checked((long)work.Counter(work.Key(2)));
	});
	private static long ObserveMutationCount(LightningDatabase provider) => provider.Store.Read(tx => tx.Count(AtomicWikiLayout.Mutations));
	private static AtomicWikiActivation ObserveActivation(LightningDatabase provider, Guid id) => provider.Store.Read(tx =>
	{
		var reader = (IBoundedReadTx)tx;
		if (reader.ReadValue(Tables.Meta, AtomicWikiLayout.MarkerKey, new NativeReadBudget(8, 4096, 64 * 1024, CancellationToken.None)) is not NativeReadValue marker)
			throw new InvalidOperationException("Expected real activation marker.");
		using (marker) return AtomicWikiLayout.Decode(marker.Value, id);
	});
	private static ulong ObserveRawEpoch(LightningDatabase provider) => provider.Store.Read(tx =>
	{
		if (((IBoundedReadTx)tx).ReadValue(Tables.Meta, AtomicWikiLayout.RawEpochKey, new NativeReadBudget(8, 4096, 64 * 1024, CancellationToken.None)) is not NativeReadValue epoch) return 0UL;
		using (epoch) return BinaryPrimitives.ReadUInt64BigEndian(epoch.Value);
	});
	private static string WikiFingerprint(LightningDatabase provider, bool active) => provider.Store.Read(tx =>
	{
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		var budget = new NativeReadBudget(2048, 128 * 1024, 4 * 1024 * 1024, CancellationToken.None);
		foreach (var table in new[] { Tables.Meta, Tables.WikiPage, Tables.WikiSlug, Tables.WikiRev, Tables.WikiRequirement, Tables.WikiPin, Tables.WikiTr, Tables.PkgWiki }
			.Concat(active ? AtomicWikiLayout.All : []))
		{
			hash.AppendData(Encoding.UTF8.GetBytes(table.Name));
			using var rows = ((IBoundedReadTx)tx).ReadRange(table, [], [], [], [], budget);
			if (!rows.Exhausted) throw new InvalidOperationException("Fixture snapshot exceeds its bounded section.");
			foreach (var row in rows.Entries) { hash.AppendData(row.Key); hash.AppendData(row.Value); }
		}
		return Convert.ToHexStringLower(hash.GetHashAndReset());
	});
	private static async Task WaitForLease(LightningDatabase provider)
	{
		using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		while (provider.AtomicWikiLegacyMutationsInFlight == 0) await Task.Delay(1, deadline.Token);
	}
	private static async Task AssertFixtureFailure(Func<Task> operation, WikiFixtureMutationFailure expected)
	{
		WikiFixtureMutationFailure? reason = null;
		try { await operation(); }
		catch (WikiFixtureMutationException error) { reason = error.Reason; }
		await Assert.That(reason == expected).IsTrue();
	}
}
