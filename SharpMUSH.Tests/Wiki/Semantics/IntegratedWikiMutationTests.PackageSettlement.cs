using System.Buffers.Binary;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Wiki.Semantics;

public partial class IntegratedWikiMutationTests
{
	private static readonly byte[] PackageSettlementKey = Keys.Str("prototype.semantic.package-settlement/v1");
	private static byte[] PackageState(LightningDatabase provider) => provider.Store.Read(tx =>
		tx.TryGet(Tables.Meta, PackageSettlementKey, out var value) ? value : []);
	private static byte[] UnknownPackageState(Guid identity, ulong generation)
	{
		var bytes = new byte[32];
		"FPKG"u8.CopyTo(bytes);
		bytes[4] = 1;
		identity.TryWriteBytes(bytes.AsSpan(8, 16), bigEndian: true, out _);
		BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(24), generation);
		return bytes;
	}

	[Test]
	public async Task FixturePackageStateStartsUnknownAndReopenCannotInventSettlement()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 0))).IsTrue();
		await host.CloseAsync();
		await host.OpenAsync();
		await Assert.That(PackageState(host.Services.GetRequiredService<LightningDatabase>()).SequenceEqual(UnknownPackageState(host.FixtureId, 0))).IsTrue();
	}

	[Test]
	public async Task FixturePackageDirectProviderWritesAdvanceDurableInvalidation()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		await provider.UpsertInstalledPackageAsync(new InstalledPackageRecord("settlement-test", "1.0.0",
			"https://example.invalid/owned", null, "literal-commit", null, DateTimeOffset.UnixEpoch, 1));
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 1))).IsTrue();
		await provider.UpsertPackageObjectAsync(new PackageObjectRecord("settlement-test", "marker", "#1:1", "thing"));
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 2))).IsTrue();
		await host.CloseAsync();
		await host.OpenAsync();
		var reopened = host.Services.GetRequiredService<LightningDatabase>();
		await Assert.That(PackageState(reopened).SequenceEqual(UnknownPackageState(host.FixtureId, 2))).IsTrue();
		await Assert.That((await reopened.GetPackageObjectsAsync("settlement-test")).Single().Ref).IsEqualTo("marker");
	}

	[Test]
	public async Task FixturePackageEveryNativeTableAndMutatorInvalidatesOncePerCallback()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var tables = new[] { Tables.Pkg, Tables.PkgObj, Tables.PkgAttr, Tables.PkgStruct,
			Tables.PkgWiki, Tables.PkgRemote, Tables.PkgRev, Tables.PkgDep };
		ulong generation = 0;
		foreach (var table in tables)
		{
			// Alternate reconstructed definitions; classification must use the stable table name.
			var alias = table == Tables.PkgDep ? TableDef.Index(table.Name, duplicates: true) : TableDef.Node(table.Name);
			await provider.Store.WriteAsync(tx => { tx.Put(alias, [111], [118]); tx.Put(alias, [112], [119]); });
			await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, ++generation))).IsTrue();
		}
		await provider.Store.WriteAsync(tx => tx.Delete(Tables.Pkg, [111]));
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, ++generation))).IsTrue();
		await provider.Store.WriteAsync(tx => tx.Delete(Tables.PkgDep, [111], [118]));
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, ++generation))).IsTrue();
		await provider.Store.WriteAsync(tx => tx.DeletePrefix(Tables.PkgObj, [255]));
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, ++generation))).IsTrue();
		await provider.Store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("ordinary-literal"), [42]));
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, generation))).IsTrue();
	}

	[Test]
	public async Task FixturePackageCaughtMarkerTamperingCannotCommitOtherWrites()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		await Assert.That(async () => await provider.Store.WriteAsync(tx =>
		{
			try { tx.Put(Tables.Meta, PackageSettlementKey, [99]); }
			catch (WikiFixtureMutationException) { }
			tx.Put(Tables.Meta, Keys.Str("must-not-commit"), [1]);
		})).Throws<WikiFixtureMutationException>();
		await Assert.That(provider.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("must-not-commit"), out _))).IsFalse();
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 0))).IsTrue();
	}
	[Test]
	public async Task FixturePackageGroupedChildrenPersistOnlySuccessfulInvalidations()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var store = provider.Store;
		using var release = new ManualResetEventSlim(false);
		var started = NewSignal();
		var blocker = Task.Run(async () => await store.WriteAsync(tx =>
		{
			started.TrySetResult();
			if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Owned batch barrier");
			tx.Put(Tables.Meta, Keys.Str("owned-batch-barrier"), [1]);
		}));
		Task[] work;
		long before;
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
			before = store.CommitCount;
			work = [
				store.WriteAsync(tx => tx.Put(Tables.PkgRemote, [101], [1])).AsTask(),
				store.WriteAsync(tx => { tx.Put(Tables.PkgRemote, [102], [2]); throw new ArithmeticException("Owned child abort"); }).AsTask(),
				store.WriteAsync(tx => tx.Put(Tables.PkgRemote, [103], [3])).AsTask()];
		}
		finally { release.Set(); await blocker; }
		await Task.WhenAll(work[0], work[2]);
		await Assert.That(async () => await work[1]).Throws<ArithmeticException>();
		await Assert.That(store.CommitCount - before).IsEqualTo(2);
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 2))).IsTrue();
		await Assert.That(store.Read(tx => tx.TryGet(Tables.PkgRemote, [102], out _))).IsFalse();
		await host.CloseAsync();
		await host.OpenAsync();
		var reopened = host.Services.GetRequiredService<LightningDatabase>();
		await Assert.That(PackageState(reopened).SequenceEqual(UnknownPackageState(host.FixtureId, 2))).IsTrue();
		await Assert.That(reopened.Store.Read(tx => tx.TryGet(Tables.PkgRemote, [101], out var a) && a.SequenceEqual(new byte[] { 1 })
			&& tx.TryGet(Tables.PkgRemote, [103], out var b) && b.SequenceEqual(new byte[] { 3 })
			&& !tx.TryGet(Tables.PkgRemote, [102], out _))).IsTrue();
	}

	[Test]
	public async Task FixturePackageRawDelegatesAndDirectoryReplacementRefuseBeforeTouchingWorld()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		var entered = false;
		await Assert.That(() => provider.Store.WriteRaw<bool>(_ => { entered = true; throw new ArithmeticException("Must never execute"); }))
			.Throws<WikiFixtureMutationException>();
		await Assert.That(entered).IsFalse();
		await Assert.That(() => provider.Store.WipeDirectory()).Throws<WikiFixtureMutationException>();
		await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 0))).IsTrue();
		await host.CloseAsync();
		await host.OpenAsync();
		await Assert.That(PackageState(host.Services.GetRequiredService<LightningDatabase>())
			.SequenceEqual(UnknownPackageState(host.FixtureId, 0))).IsTrue();
	}

	[Test]
	public async Task FixturePackageEveryProtectedMutationPoisonsCaughtRefusals()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var provider = host.Services.GetRequiredService<LightningDatabase>();
		foreach (var key in new[] { PackageSettlementKey, Keys.Str("prototype.semantic.fixture.identity/v1") })
			for (var variant = 0; variant < 4; variant++)
			{
				await Assert.That(async () => await provider.Store.WriteAsync(tx =>
				{
					try
					{
						switch (variant)
						{
							case 0: tx.Put(Tables.Meta, key, [99]); break;
							case 1: tx.Delete(Tables.Meta, key); break;
							case 2: tx.Delete(Tables.Meta, key, [99]); break;
							case 3: tx.DeletePrefix(Tables.Meta, []); break;
						}
					}
					catch (WikiFixtureMutationException) { }
					tx.Put(Tables.Meta, Keys.Str("poisoned-callback"), [1]);
				})).Throws<WikiFixtureMutationException>();
				await Assert.That(provider.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("poisoned-callback"), out _))).IsFalse();
				await Assert.That(PackageState(provider).SequenceEqual(UnknownPackageState(host.FixtureId, 0))).IsTrue();
			}
	}

	[Test]
	public async Task FixturePackageMalformedMarkerRefusesReadOnlyBeforeWritingConstructor()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var original = PackageState(host.Services.GetRequiredService<LightningDatabase>());
		await host.CloseAsync();
		var options = new LightningStoreOptions { Path = host.WorldPath };
		for (var variant = 0; variant < 9; variant++)
		{
			var corrupt = (byte[])original.Clone();
			switch (variant)
			{
				case 1: corrupt = []; break;
				case 2: corrupt = new byte[31]; break;
				case 3: corrupt = new byte[33]; break;
				case 4: corrupt[0] = 0; break;
				case 5: corrupt[4] = 2; break;
				case 6: corrupt[5] = 2; break;
				case 7: corrupt[8] ^= 255; break;
				case 8: corrupt[6] = 1; break;
			}
			// Deliberate corruption of one fully closed, exclusively owned disposable lineage.
			// Ordinary Store here is an offline test setup tool, not a new fixture issuer.
			using (var setup = new LightningStore(options))
				await setup.WriteAsync(tx =>
				{
					if (variant == 0) tx.Delete(Tables.Meta, PackageSettlementKey);
					else tx.Put(Tables.Meta, PackageSettlementKey, corrupt);
				});
			var before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Join(host.WorldPath, "data.mdb")));
			await Assert.That(() => LightningStore.RequireFixtureIdentity(options, host.FixtureId)).Throws<InvalidOperationException>();
			var after = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Join(host.WorldPath, "data.mdb")));
			await Assert.That(after.SequenceEqual(before)).IsTrue();
		}
	}

}
