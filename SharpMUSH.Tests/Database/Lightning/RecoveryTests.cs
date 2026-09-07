using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Durability: an uncommitted transaction leaves nothing behind, and a hot copy of the environment
/// opens as a store carrying the same data.
/// </summary>
public class RecoveryTests
{
	private static string TempPath() => Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));

	private static void Delete(string path)
	{
		if (!Directory.Exists(path)) return;
		try
		{
			Directory.Delete(path, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort: a lingering mdb.lck can outlive the writer thread's join.
		}
	}

	[Test]
	public async Task AnAbandonedWriteIsAbsentAfterReopen()
	{
		var path = TempPath();
		var options = new LightningStoreOptions { Path = path, MapSize = 256L << 20 };
		try
		{
			using (var store = new LightningStore(options))
			{
				await store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("kept"), Keys.Str("1")));
				// Simulate a crash mid-transaction: a job that throws after writing is rolled back.
				try
				{
					await store.WriteAsync<int>(tx =>
					{
						tx.Put(Tables.Meta, Keys.Str("lost"), Keys.Str("1"));
						throw new Exception("crash");
					});
				}
				catch (Exception ex) when (ex.Message == "crash")
				{
					// Expected: the faulted job's transaction is never committed.
				}
			}

			using var reopened = new LightningStore(options);
			await Assert.That(reopened.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("kept"), out _))).IsTrue();
			await Assert.That(reopened.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("lost"), out _))).IsFalse();
		}
		finally
		{
			Delete(path);
		}
	}

	[Test]
	public async Task CopyToAsyncProducesADirectoryThatOpensWithTheSameObjectCount()
	{
		var path = TempPath();
		var backupPath = TempPath();
		// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
		var db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(),
			relations: null);
		try
		{
			await db.Migrate();
			var objects = db.Store.Count(Tables.Obj);

			await db.CopyToAsync(backupPath);

			using var backup = new LightningStore(new LightningStoreOptions { Path = backupPath, MapSize = 256L << 20 });
			await Assert.That(backup.Count(Tables.Obj)).IsEqualTo(objects);
			var godName = backup.Read(tx => tx.TryGet(Tables.Obj, Keys.Dbref(1), out var v)
				? Codec.Deserialize<ObjectRecord>(v).Name
				: null);
			await Assert.That(godName).IsEqualTo("God");
		}
		finally
		{
			db.Store.Dispose();
			Delete(path);
			Delete(backupPath);
		}
	}

	[Test]
	public async Task WipeEmptiesTheDirectoryAndReseeds()
	{
		var path = TempPath();
		// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
		var db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(),
			relations: null);
		try
		{
			await db.Migrate();
			await db.Store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("wipe_marker"), Keys.Str("1")));

			await db.WipeDatabaseAsync();

			await Assert.That(db.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("wipe_marker"), out _))).IsFalse();
			await Assert.That(db.Store.Count(Tables.Obj)).IsEqualTo(10);
		}
		finally
		{
			db.Store.Dispose();
			Delete(path);
		}
	}
}
