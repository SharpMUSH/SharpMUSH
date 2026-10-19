using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

public partial class IntegratedWikiMutationTests
{
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task ActiveNulAliasOpenRejectsBeforeNativeIdentityLookup(bool meta)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		await host.ActivateAtomicWikiProjectionAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var accessor = host.Services.GetRequiredService<ILightningStorageAccessor>();
		var before = WikiFingerprint(provider, true);
		var epoch = ObserveRawEpoch(provider);
		var rejected = false;
		try { accessor.OpenTable((meta ? Tables.Meta.Name : Tables.WikiPage.Name) + "\0plugin", false); }
		catch (ArgumentException) { rejected = true; }
		var after = WikiFingerprint(provider, true);
		var afterEpoch = ObserveRawEpoch(provider);
		await host.CloseAsync();
		await host.OpenAsync();
		await Assert.That(WikiFingerprint(host.Services.GetRequiredService<LightningDatabase>(), true)).IsEqualTo(before);
		await Assert.That(after).IsEqualTo(before);
		await Assert.That(afterEpoch).IsEqualTo(epoch);
		await Assert.That(rejected).IsTrue();
	}

	[Test]
	[Arguments(true, true, 0)]
	[Arguments(true, true, 1)]
	[Arguments(true, true, 2)]
	[Arguments(true, true, 3)]
	[Arguments(false, true, 0)]
	[Arguments(false, true, 1)]
	[Arguments(false, true, 2)]
	[Arguments(false, true, 3)]
	[Arguments(true, false, 0)]
	[Arguments(true, false, 1)]
	[Arguments(true, false, 2)]
	[Arguments(true, false, 3)]
	[Arguments(false, false, 0)]
	[Arguments(false, false, 1)]
	[Arguments(false, false, 2)]
	[Arguments(false, false, 3)]
	public async Task NulAliasMutationPoisonsCaughtViolationAndRollsBackPluginWrite(bool meta, bool registerInactive, int mutation)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var accessor = host.Services.GetRequiredService<ILightningStorageAccessor>();
		var plugin = accessor.OpenTable("fixture.alias.plugin", false);
		var aliasName = (meta ? Tables.Meta.Name : Tables.WikiPage.Name) + "\0plugin";
		var alias = TableDef.Node(aliasName);
		if (registerInactive)
		{
			// After name validation is fixed, setup may reject early. The forged definition still must poison.
			try { alias = accessor.OpenTable(aliasName, false); }
			catch (ArgumentException)
			{
				// Rejecting alias registration is expected; the forged definition still exercises poisoning.
			}
		}
		var existing = provider.Store.Read(tx =>
		{
			using var rows = ((IBoundedReadTx)tx).ReadRange(Tables.WikiPage, [], [], [], [],
				new NativeReadBudget(32, 128 * 1024, 4 * 1024 * 1024, CancellationToken.None));
			return rows.Entries[0];
		});
		// All attempted baseline writes are harmless even while the reviewed guard is still broken.
		var absent = meta ? Encoding.UTF8.GetBytes("fixture.nul-alias.probe-" + Guid.NewGuid().ToString("N"))
			: existing.Key.Concat(new byte[] { 255 }).ToArray();
		var putKey = meta ? absent : existing.Key;
		var putValue = meta ? new byte[] { 1 } : existing.Value;
		await Assert.That(accessor.Read(tx => tx.TryGet(meta ? Tables.Meta : Tables.WikiPage, absent, out _))).IsFalse();
		await host.ActivateAtomicWikiProjectionAsync();
		var before = WikiFingerprint(provider, true);
		var epoch = ObserveRawEpoch(provider);
		var rejected = false;
		try
		{
			await accessor.WriteAsync(tx =>
			{
				tx.Put(plugin, [7], [8]);
				try
				{
					switch (mutation)
					{
						case 0: tx.Put(alias, putKey, putValue); break;
						case 1: tx.Delete(alias, absent); break;
						case 2: tx.Delete(alias, absent, putValue); break;
						case 3: tx.DeletePrefix(alias, absent); break;
						default: throw new ArgumentOutOfRangeException(nameof(mutation));
					}
				}
				catch (Exception error) when (error is WikiFixtureMutationException or ArgumentException or KeyNotFoundException)
				{
					// An unregistered forged definition can miss the handle map before the eventual fix;
					// catching that miss must not make an invalid alias an accepted plugin mutation.
				}
				return true;
			});
		}
		catch (WikiFixtureMutationException error) when (error.Reason == WikiFixtureMutationFailure.UnsupportedOperation) { rejected = true; }
		var after = WikiFingerprint(provider, true);
		var afterEpoch = ObserveRawEpoch(provider);
		var pluginWritten = accessor.Read(tx => tx.TryGet(plugin, new byte[] { 7 }, out _));
		await host.CloseAsync();
		await host.OpenAsync();
		var reopenedProvider = host.Services.GetRequiredService<LightningDatabase>();
		var reopenedAccessor = host.Services.GetRequiredService<ILightningStorageAccessor>();
		var reopenedPlugin = reopenedAccessor.OpenTable(plugin.Name, false);
		var reopenedPluginWritten = reopenedAccessor.Read(tx => tx.TryGet(reopenedPlugin, new byte[] { 7 }, out _));
		await Assert.That(after).IsEqualTo(before);
		await Assert.That(WikiFingerprint(reopenedProvider, true)).IsEqualTo(before);
		await Assert.That(afterEpoch).IsEqualTo(epoch);
		await Assert.That(ObserveRawEpoch(reopenedProvider)).IsEqualTo(epoch);
		await Assert.That(pluginWritten).IsFalse();
		await Assert.That(reopenedPluginWritten).IsFalse();
		await Assert.That(rejected).IsTrue();
	}

	[Test]
	public async Task ActivationAggregateRecordAdmissionStopsBeforeRecord129AcrossPageRangeAndSlugPoints()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = await PopulateSmallActualPages(provider, wiki, 80);
		await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.WorkLimit);
		var observation = provider.AtomicWikiPreflightRead ?? throw new InvalidOperationException("Expected actual preflight native-read observation.");
		await Assert.That(observation.Completed).IsFalse();
		await Assert.That(observation.LimitDimension).IsEqualTo("records");
		await Assert.That(observation.Bytes).IsLessThan(128L * 1024);
		await Assert.That(observation.WorkingBytes).IsLessThan(4L * 1024 * 1024);
		await AssertRejectedActivationKeepsLegacyWritable(host, provider, wiki, page);
		// Actual checked native copy-admission count, not a count inferred from page inputs.
		await Assert.That(observation.Records).IsLessThanOrEqualTo(128);
	}

	[Test]
	public async Task ActivationAggregateRecordAdmissionIncludesFinalNextWikiPointBeforeCopy()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = await PopulateSmallActualPages(provider, wiki, 40);
		var pages = provider.Store.Read(tx => tx.Count(Tables.WikiPage));
		var requirements = provider.Store.Read(tx => tx.Count(Tables.WikiRequirement));
		var pins = provider.Store.Read(tx => tx.Count(Tables.WikiPin));
		var additionalPins = checked((int)(128 - 3 * pages - requirements - pins));
		await Assert.That(additionalPins).IsGreaterThanOrEqualTo(0);
		for (var i = 0; i < additionalPins; i++) await wiki.SetCategoryPinnedAsync("record-budget-pin-" + i, true);
		var precedingCopies = 3 * provider.Store.Read(tx => tx.Count(Tables.WikiPage))
			+ provider.Store.Read(tx => tx.Count(Tables.WikiRequirement)) + provider.Store.Read(tx => tx.Count(Tables.WikiPin));
		await Assert.That(precedingCopies).IsEqualTo(128L);
		await AssertFixtureFailure(() => host.ActivateAtomicWikiProjectionAsync(), WikiFixtureMutationFailure.WorkLimit);
		var observation = provider.AtomicWikiPreflightRead ?? throw new InvalidOperationException("Expected actual final-point failure observation.");
		await Assert.That(observation.Completed).IsFalse();
		await Assert.That(observation.LimitDimension).IsEqualTo("records");
		await Assert.That(observation.Bytes).IsLessThan(128L * 1024);
		await Assert.That(observation.WorkingBytes).IsLessThan(4L * 1024 * 1024);
		await AssertRejectedActivationKeepsLegacyWritable(host, provider, wiki, page);
		await Assert.That(observation.Records).IsLessThanOrEqualTo(128);
	}

	[Test]
	public async Task ActualActivationPublishesSeparateCompletedNativeReadSections()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var pages = provider.Store.Read(tx => tx.Count(Tables.WikiPage));
		var requirements = provider.Store.Read(tx => tx.Count(Tables.WikiRequirement));
		var pins = provider.Store.Read(tx => tx.Count(Tables.WikiPin));
		await host.ActivateAtomicWikiProjectionAsync();
		var preflight = provider.AtomicWikiPreflightRead ?? throw new InvalidOperationException("Expected completed real preflight observation.");
		var final = provider.AtomicWikiFinalValidationRead ?? throw new InvalidOperationException("Expected completed real final-validation observation.");
		await Assert.That(preflight.Completed).IsTrue();
		await Assert.That(final.Completed).IsTrue();
		await Assert.That(preflight.LimitDimension).IsNull();
		await Assert.That(final.LimitDimension).IsNull();
		await Assert.That((long)preflight.Records).IsEqualTo(3 * pages + requirements + pins + 1);
		await Assert.That((long)final.Records).IsEqualTo(2 * pages + requirements + pins + 1);
		await Assert.That(preflight.Records).IsLessThanOrEqualTo(128);
		await Assert.That(final.Records).IsLessThanOrEqualTo(128);
	}

	private static async Task<WikiPage> PopulateSmallActualPages(LightningDatabase provider, IWikiService wiki, int totalPages)
	{
		var initial = provider.Store.Read(tx => tx.Count(Tables.WikiPage));
		WikiPage? last = null;
		for (var i = initial; i < totalPages; i++)
			last = (await wiki.CreateAsync("b" + i, "b", "#1", sourceLocale: "en")).Expect<WikiPage>();
		await Assert.That(provider.Store.Read(tx => tx.Count(Tables.WikiPage))).IsEqualTo((long)totalPages);
		return last ?? throw new InvalidOperationException("Expected an actual small page creation.");
	}

	private static async Task AssertRejectedActivationKeepsLegacyWritable(SemanticWikiPrototypeHost host, LightningDatabase provider,
		IWikiService wiki, WikiPage page)
	{
		await Assert.That(provider.AtomicWikiPhase).IsEqualTo(SemanticFixturePhase.Inactive);
		await Assert.That(provider.Store.Read(tx => tx.TryGet(Tables.Meta, AtomicWikiLayout.MarkerKey, out _))).IsFalse();
		var edited = (await wiki.UpdateAsync(page.Id, "after rejected activation", "#1")).Expect<WikiPage>();
		await Assert.That(edited.RevisionNumber).IsEqualTo(page.RevisionNumber + 1);
		await host.CloseAsync();
		await AssertNoSemanticTables(host.WorldPath);
		await host.OpenAsync();
		await Assert.That((await host.Services.GetRequiredService<IWikiService>().GetByIdAsync(page.Id)).Expect<WikiPage>().MarkdownSource)
			.IsEqualTo("after rejected activation");
		await host.CloseAsync();
		await AssertNoSemanticTables(host.WorldPath);
	}
}
