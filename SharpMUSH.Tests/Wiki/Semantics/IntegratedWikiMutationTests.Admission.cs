using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

public partial class IntegratedWikiMutationTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task RawWriterEntryLeasePreventsActivationUntilActualCompletion(bool openTable)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var accessor = host.Services.GetRequiredService<ILightningStorageAccessor>();
		await provider.Store.PauseAndDrainWriterAsync();
		Task? parent = null;
		try
		{
			parent = openTable
				? Task.Run(() => accessor.OpenTable("fixture.paused.raw", false))
				: accessor.WriteAsync(tx => true).AsTask();
			await WaitForLease(provider);
			await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
			using var cancellation = new CancellationTokenSource();
			var waiter = parent.WaitAsync(cancellation.Token);
			cancellation.Cancel();
			await Assert.That(() => waiter).Throws<OperationCanceledException>();
			await Assert.That(parent.IsCompleted).IsFalse();
			await Assert.That(provider.AtomicWikiLegacyMutationsInFlight).IsEqualTo(1);
		}
		finally
		{
			provider.Store.ResumeWriter();
			if (parent is not null) await parent;
		}
		await Assert.That(provider.AtomicWikiLegacyMutationsInFlight).IsEqualTo(0);
		await host.ActivateAtomicWikiProjectionAsync();
	}

	[Test]
	public async Task EveryInactiveTypedWikiEntryHoldsLeaseThroughItsOwningWriter()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = (await wiki.CreateAsync("All typed lease entries", "initial", "#1", sourceLocale: "en")).Expect<WikiPage>();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		foreach (var operation in new Func<Task>[]
		{
			async () => { await wiki.CreateAsync("Additional leased creation", "created", "#1", sourceLocale: "en"); },
			async () => { await wiki.UpdateAsync(page.Id, "changed", "#1"); },
			async () => { await wiki.SetTitleAsync(page.Id, "Title while leased"); },
			async () => { await wiki.SetMetadataAsync(page.Id, ["lease"], false); },
			async () => { await wiki.SetRequirementsAsync(WikiRuleTarget.ForCategory("lease"), WikiRequirementSet.Protection, "#1"); },
			async () => { await wiki.SetCategoryPinnedAsync("lease", true); },
			async () => { await wiki.UpsertTranslationAsync(page.Id, "fr", "Titre", "translated", "#1", null, true, null); },
			async () => { await wiki.DeleteTranslationAsync(page.Id, "fr", "#1"); },
			async () => { await wiki.DeleteAsync(page.Id, "#1"); }
		})
		{
			await provider.Store.PauseAndDrainWriterAsync();
			Task? parent = null;
			try
			{
				parent = operation();
				await WaitForLease(provider);
				await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.Unavailable);
				await Assert.That(parent.IsCompleted).IsFalse();
			}
			finally
			{
				provider.Store.ResumeWriter();
				if (parent is not null) await parent;
			}
			await Assert.That(provider.AtomicWikiLegacyMutationsInFlight).IsEqualTo(0);
		}
		await host.ActivateAtomicWikiProjectionAsync();
	}

	[Test]
	public async Task CancellationBeforeLayoutRestoresInactiveAndAllowsCleanRetry()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.That(() => host.ActivateAtomicWikiProjectionAsync(cancellation.Token)).Throws<OperationCanceledException>();
		await Assert.That(provider.AtomicWikiPhase).IsEqualTo(SemanticFixturePhase.Inactive);
		await host.CloseAsync();
		await AssertNoSemanticTables(host.WorldPath);
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
	}
}
