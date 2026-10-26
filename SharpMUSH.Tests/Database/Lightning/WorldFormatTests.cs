using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

public class WorldFormatTests
{
	[Test]
	public async Task CoreMigrationFormatsStartAtOneAndNeverSkip()
	{
		var formats = LightningDatabase.CoreMigrations.Select(migration => migration.Format).ToArray();
		var steps = formats.Zip(formats.Skip(1), (before, after) => after - before).ToArray();

		await Assert.That(formats[0]).IsEqualTo(1);
		await Assert.That(steps.All(step => step is 0 or 1)).IsTrue()
			.Because("a migration added at the end keeps the format or takes the next one, never an older or a skipped one");
		await Assert.That(LightningDatabase.WorldFormat).IsEqualTo(formats[^1]);
	}

	[Test]
	public async Task EveryMigrationAWorldRecordsIsACoreMigration()
	{
		var path = NewPath();
		var db = Create(path);
		try
		{
			await db.Migrate();

			var recorded = db.Store.Read(tx => tx.Range(Tables.Meta, Keys.Str("mig:"))
				.Select(entry => Codec.Deserialize<MigrationRecord>(entry.Value).Id).ToArray());
			await Assert.That(recorded).IsEquivalentTo(LightningDatabase.CoreMigrations.Select(migration => migration.Id).ToArray())
				.Because("a migration that records a marker without a place in CoreMigrations changes the world without moving its format");
		}
		finally
		{
			await db.DisposeAsync();
			await FixtureDirectoryCleanup.DeleteAsync(path);
		}
	}

	[Test]
	public async Task MigrateStampsTheWorldWithTheCurrentFormat()
	{
		var path = NewPath();
		var db = Create(path);
		try
		{
			await db.Migrate();

			await Assert.That(db.Store.Read(LightningDatabase.ReadWorldFormat)).IsEqualTo(LightningDatabase.WorldFormat);
		}
		finally
		{
			await db.DisposeAsync();
			await FixtureDirectoryCleanup.DeleteAsync(path);
		}
	}

	[Test]
	public async Task AWorldWithNoStampIsStampedOnItsNextStart()
	{
		var path = NewPath();
		var db = Create(path);
		try
		{
			await db.Migrate();
			await db.Store.WriteAsync(tx => tx.Delete(Tables.Meta, LightningDatabase.WorldFormatKey));

			await db.Migrate();

			await Assert.That(db.Store.Read(LightningDatabase.ReadWorldFormat)).IsEqualTo(LightningDatabase.WorldFormat);
		}
		finally
		{
			await db.DisposeAsync();
			await FixtureDirectoryCleanup.DeleteAsync(path);
		}
	}

	[Test]
	public async Task AWorldFromANewerServerIsRefusedBeforeAnythingIsWritten()
	{
		var path = NewPath();
		var db = Create(path);
		try
		{
			await db.Migrate();
			var newer = LightningDatabase.WorldFormat + 1;
			await db.Store.WriteAsync(tx =>
			{
				var value = new byte[sizeof(int)];
				BinaryPrimitives.WriteInt32BigEndian(value, newer);
				tx.Put(Tables.Meta, LightningDatabase.WorldFormatKey, value);
				tx.Delete(Tables.Meta, Keys.Str("next_dbref"));
			});

			var refusal = await Assert.ThrowsAsync<InvalidOperationException>(async () => await db.Migrate());

			await Assert.That(refusal!.Message).Contains($"format {newer}");
			await Assert.That(db.Store.Read(LightningDatabase.ReadWorldFormat)).IsEqualTo(newer);
			await Assert.That(db.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("next_dbref"), out _))).IsFalse()
				.Because("a refused world is not migrated: next_dbref would have been recomputed");
		}
		finally
		{
			await db.DisposeAsync();
			await FixtureDirectoryCleanup.DeleteAsync(path);
		}
	}

	private static string NewPath() => Path.Join(Path.GetTempPath(), "world-format-" + Guid.NewGuid().ToString("N"));

	// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
}
