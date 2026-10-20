using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

public partial class IntegratedWikiMutationTests
{
	[Test]
	public async Task FixtureUnsupportedWritesAreExplicitAndUnchanged()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = (await wiki.CreateAsync("Unsupported operation target", "unchanged body", "#1", sourceLocale: "en")).Expect<WikiPage>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		await host.ActivateAtomicWikiProjectionAsync();
		var before = WikiFingerprint(provider, true);
		var owner = new PackageWikiPageRecord("fixture-package", page.Id, page.Namespace, page.Slug, page.Title,
			page.MarkdownSource, page.Categories, page.Published, PackageWikiRequires.None, false, "1.0.0");
		foreach (var operation in new Func<Task>[]
		{
			async () => { await wiki.SetRequirementsAsync(WikiRuleTarget.ForPage(page.Id), WikiRequirementSet.Protection, "#1"); },
			async () => { await wiki.UpsertTranslationAsync(page.Id, "fr", "Translated target", "translation", "#1", null, true, null); },
			async () => { await wiki.DeleteTranslationAsync(page.Id, "fr", "#1"); },
			() => provider.UpsertPackageWikiPageAsync(owner),
			() => provider.RemovePackageWikiPageAsync(owner.PackageId, owner.Namespace, owner.Slug),
			() => provider.RemoveInstalledPackageAsync(owner.PackageId),
			async () => { await provider.WikiHistory.PurgeAsync(new HistoryPurgeBatch([], []), new HistoryRetentionRule { KeepNewest = 1 }, DateTimeOffset.UtcNow); }
		})
		{
			await AssertFixtureFailure(operation, WikiFixtureMutationFailure.UnsupportedOperation);
			await Assert.That(WikiFingerprint(provider, true)).IsEqualTo(before);
		}
		await wiki.SetCategoryPinnedAsync("presentation-only", true);
		await Assert.That((await wiki.GetPinnedCategoriesAsync()).Contains("presentation-only")).IsTrue();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ExistingOwnershipOrTranslationRejectsBeforeAnyActivationWrite(bool ownership)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = (await wiki.CreateAsync("Preflight ownership target", "legacy body", "#1", sourceLocale: "en")).Expect<WikiPage>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		if (ownership)
			await provider.UpsertPackageWikiPageAsync(new("fixture-package", page.Id, page.Namespace, page.Slug, page.Title,
				page.MarkdownSource, page.Categories, page.Published, PackageWikiRequires.None, false, "1.0.0"));
		else
			await wiki.UpsertTranslationAsync(page.Id, "fr", "Titre", "legacy translation", "#1", null, true, null);
		var before = WikiFingerprint(provider, false);
		await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.UnsupportedOperation);
		await Assert.That(WikiFingerprint(provider, false)).IsEqualTo(before);
		await Assert.That(provider.AtomicWikiPhase).IsEqualTo(SemanticFixturePhase.Inactive);
		await wiki.SetTitleAsync(page.Id, "Legacy title remains writable");
		await host.CloseAsync();
		await AssertNoSemanticTables(host.WorldPath);
	}

	[Test]
	public async Task DirectPackageEntriesIncludingManagedApplyRefuseBeforeInnerWork()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var before = WikiFingerprint(provider, true);
		var installer = host.Services.GetRequiredService<IPackageInstallService>();
		var files = new NeverReadPackageFiles();
		const string refusal = "Package mutations are unavailable in this atomic wiki fixture slice.";
		foreach (var kind in new[] { PackageKind.Softcode, PackageKind.Application, PackageKind.Plugin, PackageKind.Ui })
		{
			var manifest = new PackageManifest(new(1, 7), "fixture-refused", new(1, 0, 0), ["fixture"], "fixture", null,
				null, [], null, null, null, [], [], new Dictionary<string, PackageConfigureSpec>(), [], Kind: kind);
			var request = new PackageApplyRequest(new("https://example.invalid/fixture", null, "fixture-commit", null),
				new Dictionary<string, string>(), [], AllowPluginCode: true);
			var result = await installer.ApplyAsync(manifest, request, fileSource: files);
			await Assert.That(result.Expect<Error<string>>().Value).IsEqualTo(refusal);
		}
		await Assert.That((await installer.UninstallAsync("fixture-refused")).Expect<Error<string>>().Value).IsEqualTo(refusal);
		await Assert.That((await installer.RollbackAsync("fixture-refused", 1)).Expect<Error<string>>().Value).IsEqualTo(refusal);
		await Assert.That(files.Invoked).IsFalse();
		await Assert.That(WikiFingerprint(provider, true)).IsEqualTo(before);
	}

	[Test]
	public async Task CancelledRunnerAdmissionPermanentlyPreventsActivationBeforeDelegates()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var invoked = false;
		var runner = host.Services.GetRequiredService<IPackageOperationRunner>();
		await Assert.That(() => runner.RunAsync("cancelled-fixture-admission", _ => { invoked = true; return Task.FromResult(1); }, cancellation.Token))
			.Throws<OperationCanceledException>();
		await Assert.That(invoked).IsFalse();
		await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
		await host.CloseAsync();
		await AssertNoSemanticTables(host.WorldPath);
	}

	[Test]
	public async Task MissingActiveCertificateRefusesCurrentWritesAndClosedReopen()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		// Remove one valid owned fixture row after genuine activation, never manufacture successful setup.
		await provider.Store.WriteAsync(tx => tx.Delete(AtomicWikiLayout.Metadata, AtomicWikiLayout.CertificateKey));
		await AssertFixtureFailure(() => provider.WriteAsync(tx => true).AsTask(), WikiFixtureMutationFailure.Unavailable);
		await Assert.That(provider.AtomicWikiPhase).IsEqualTo(SemanticFixturePhase.Failed);
		await host.CloseAsync();
		await Assert.That(() => host.OpenAsync()).Throws<InvalidOperationException>();
	}

	private sealed class NeverReadPackageFiles : IPackageFileSource
	{
		internal bool Invoked { get; private set; }
		public Task<byte[]?> ReadFileAsync(string path, CancellationToken cancellationToken = default)
		{
			Invoked = true;
			throw new ActivationFault();
		}
	}
}
