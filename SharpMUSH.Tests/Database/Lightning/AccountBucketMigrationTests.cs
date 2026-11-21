using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Database;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>Account buckets written by development builds become attributes on their accounts' holders.</summary>
public class AccountBucketMigrationTests
{
	private readonly string _path = Path.Join(Path.GetTempPath(), "bucket-migration-" + Guid.NewGuid().ToString("N"));

	[After(Test)]
	public async Task Cleanup() => await FixtureDirectoryCleanup.DeleteAsync(_path);

	private LightningDatabase Open() => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = _path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	[Test]
	public async Task BucketsBecomeHolderAttributesWithoutOverwritingAny()
	{
		DBRef holder;
		await using (var db = Open())
		{
			await db.Migrate();
			var accountId = (await db.CreateAccountAsync("bucketeer", null, "password")).Id!;
			holder = AccountHolders.ForAccount(accountId)!.Value;
			var key = accountId.Split('/')[1];
			var god = (await db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
			await db.SetAttributeAsync(holder, ["KEPT"], MString.Plain("already here"), god);
			// As a development build left the world: buckets, and no record of this migration.
			await db.Store.WriteAsync(tx =>
			{
				tx.Put(LightningDatabaseBucketTable, Keys.Composite(key, "stats"), Keys.Str("""{"str":3}"""));
				tx.Put(LightningDatabaseBucketTable, Keys.Composite(key, "kept"), Keys.Str("""{"lost":false}"""));
				tx.Put(LightningDatabaseBucketTable, Keys.Composite(key, "bad:name"), Keys.Str("1"));
				tx.Put(LightningDatabaseBucketTable, Keys.Composite("999999", "orphan"), Keys.Str("2"));
				tx.Delete(Tables.Meta, Keys.Str("mig:" + LightningDatabase.AccountBucketMigrationId));
			});
		}

		await using (var db = Open())
		{
			await db.Migrate();
			await Assert.That((await db.GetAttributeAsync(holder, ["STATS"]).ToListAsync()).Single().Value.ToPlainText()).IsEqualTo("""{"str":3}""");
			await Assert.That((await db.GetAttributeAsync(holder, ["KEPT"]).ToListAsync()).Single().Value.ToPlainText()).IsEqualTo("already here");
			await Assert.That(db.Store.Read(tx => tx.Count(LightningDatabaseBucketTable))).IsEqualTo(3);
		}

		// A second start finds the migration done and changes nothing.
		await using (var db = Open())
		{
			await db.Migrate();
			await Assert.That(db.Store.Read(tx => tx.Count(LightningDatabaseBucketTable))).IsEqualTo(3);
			await Assert.That(await db.GetAttributeAsync(holder, ["STATS"]).CountAsync()).IsEqualTo(1);
		}
	}

	/// <summary>Seeds buckets as a development build wrote them and forgets the migration ran.</summary>
	private static async Task SeedAsync(LightningDatabase db, IEnumerable<(string AccountKey, string Name, string Json)> buckets)
		=> await db.Store.WriteAsync(tx =>
		{
			foreach (var (accountKey, name, json) in buckets)
				tx.Put(LightningDatabaseBucketTable, Keys.Composite(accountKey, name), Keys.Str(json));
			tx.Delete(Tables.Meta, Keys.Str("mig:" + LightningDatabase.AccountBucketMigrationId));
		});

	[Test]
	public async Task EveryAccountsBucketsMoveWithTheirValuesIntact()
	{
		var longJson = "{\"notes\":\"" + string.Concat(Enumerable.Repeat("über-long café note ", 20)) + "\"}";
		var holders = new List<DBRef>();
		await using (var db = Open())
		{
			await db.Migrate();
			var buckets = new List<(string, string, string)>();
			for (var i = 0; i < 3; i++)
			{
				var accountId = (await db.CreateAccountAsync($"movers{i}", null, "password")).Id!;
				holders.Add(AccountHolders.ForAccount(accountId)!.Value);
				var key = accountId.Split('/')[1];
				buckets.Add((key, "profile", longJson));
				buckets.Add((key, "char.sheet-v2", $"{{\"slot\":{i}}}"));
			}

			await SeedAsync(db, buckets);
		}

		await using (var db = Open())
		{
			await db.Migrate();
			for (var i = 0; i < holders.Count; i++)
			{
				var profile = (await db.GetAttributeAsync(holders[i], ["PROFILE"]).ToListAsync()).Single();
				await Assert.That(profile.Value.ToPlainText()).IsEqualTo(longJson);
				await Assert.That((await profile.Owner.WithCancellation(CancellationToken.None))!.Object.DBRef.Number).IsEqualTo(1);
				var sheet = (await db.GetAttributeAsync(holders[i], ["CHAR.SHEET-V2"]).ToListAsync()).Single();
				await Assert.That(sheet.Value.ToPlainText()).IsEqualTo($"{{\"slot\":{i}}}");
			}

			await Assert.That(db.Store.Read(tx => tx.Count(LightningDatabaseBucketTable))).IsEqualTo(0);
			var format = db.Store.Read(tx => tx.TryGet(Tables.Meta, LightningDatabase.WorldFormatKey.ToArray(), out var bytes)
				? System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes)
				: 0);
			await Assert.That(format).IsEqualTo(LightningDatabase.WorldFormat);
		}
	}

	[Test]
	public async Task ManyBucketsMoveInOneStart()
	{
		const int accounts = 50;
		const int perAccount = 40;
		var holders = new List<DBRef>();
		await using (var db = Open())
		{
			await db.Migrate();
			var buckets = new List<(string, string, string)>();
			for (var i = 0; i < accounts; i++)
			{
				var accountId = (await db.CreateAccountAsync($"bulk{i}", null, "password")).Id!;
				holders.Add(AccountHolders.ForAccount(accountId)!.Value);
				var key = accountId.Split('/')[1];
				for (var j = 0; j < perAccount; j++) buckets.Add((key, $"b{j}", $"{{\"n\":{i * perAccount + j}}}"));
			}

			await SeedAsync(db, buckets);
		}

		await using (var db = Open())
		{
			await db.Migrate();
			await Assert.That(db.Store.Read(tx => tx.Count(LightningDatabaseBucketTable))).IsEqualTo(0);
			foreach (var (holder, i) in holders.Select((holder, i) => (holder, i)))
			{
				var first = (await db.GetAttributeAsync(holder, ["B0"]).ToListAsync()).Single();
				await Assert.That(first.Value.ToPlainText()).IsEqualTo($"{{\"n\":{i * perAccount}}}");
				var last = (await db.GetAttributeAsync(holder, [$"B{perAccount - 1}"]).ToListAsync()).Single();
				await Assert.That(last.Value.ToPlainText()).IsEqualTo($"{{\"n\":{i * perAccount + perAccount - 1}}}");
			}
		}
	}

	[Test]
	public async Task AWorldWithNoBucketsOnlyRecordsTheMigration()
	{
		await using var db = Open();
		await db.Migrate();
		await db.Store.WriteAsync(tx => tx.Delete(Tables.Meta, Keys.Str("mig:" + LightningDatabase.AccountBucketMigrationId)));

		await db.Migrate();

		await Assert.That(db.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("mig:" + LightningDatabase.AccountBucketMigrationId), out _)))
			.IsTrue();
		await Assert.That(db.Store.Read(tx => tx.Count(LightningDatabaseBucketTable))).IsEqualTo(0);
	}

	private static TableDef LightningDatabaseBucketTable => Tables.AccountBucket;
}
