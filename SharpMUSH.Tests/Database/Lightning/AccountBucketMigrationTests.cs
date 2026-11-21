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

	private static TableDef LightningDatabaseBucketTable => Tables.AccountBucket;
}
