using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

public class MigrationTests
{
	// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	[Test]
	public async Task MigrateSeedsTheWorldOnceAndIsIdempotent()
	{
		var path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		var db = Create(path);

		try
		{
			await db.Migrate();
			await db.Migrate();

			await Assert.That(db.Store.Count(Tables.Obj)).IsEqualTo(10);
			await Assert.That(db.Store.Count(Tables.Flag)).IsEqualTo(62);
			await Assert.That(db.Store.Count(Tables.AttrEntry)).IsEqualTo(216);

			var next = db.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("next_dbref"), out var v) ? Keys.ReadDbref(v) : -1);
			await Assert.That(next).IsEqualTo(10);

			var godName = db.Store.Read(tx => tx.TryGet(Tables.Obj, Keys.Dbref(1), out var v)
				? Codec.Deserialize<ObjectRecord>(v).Name
				: null);
			await Assert.That(godName).IsEqualTo("God");
		}
		finally
		{
			await db.DisposeAsync();
			if (Directory.Exists(path))
			{
				try
				{
					Directory.Delete(path, recursive: true);
				}
				catch (IOException)
				{
					// Best-effort: a lingering LMDB lock file (mdb.lck) can outlive the writer thread's
					// join by a few milliseconds under load. Leaving the temp directory behind costs
					// disk, not correctness — matches ServerWebAppFactory's own cleanup.
				}
			}
		}
	}

	/// <summary>
	/// PennMUSH renames Pueblo_Send to Send_OOB at load (<c>src/flags.c:850-855</c>) by rewriting the
	/// FLAG struct in place, so grants follow the rename for free. Grants here are edges keyed by the
	/// power's name, so migration has to move them and drop the superseded record — otherwise a world
	/// seeded before the rename keeps a grant pointing at an orphan and the holder silently loses the
	/// power.
	/// </summary>
	[Test]
	public async Task MigrateMovesLegacyPuebloSendGrantsOntoSendOob()
	{
		var path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		var db = Create(path);

		try
		{
			await db.Migrate();

			// A world seeded before the rename: the old record, and God holding it.
			await db.Store.WriteAsync(tx =>
			{
				tx.Put(Tables.Power, Keys.Upper("Pueblo_Send"), Codec.Serialize(new PowerRecord
				{
					Name = "Pueblo_Send",
					Alias = "",
					Symbol = "",
					SetPermissions = ["wizard", "log"],
					UnsetPermissions = ["wizard"],
					TypeRestrictions = [],
					System = true,
					Disabled = false
				}));
				tx.Put(Tables.ObjPower.Forward, Keys.Dbref(1), Keys.Upper("Pueblo_Send"));
				tx.Put(Tables.ObjPower.Reverse, Keys.Upper("Pueblo_Send"), Keys.Dbref(1));
				return true;
			});

			await db.Migrate();

			var held = db.Store.Read(tx =>
				tx.Dups(Tables.ObjPower.Forward, Keys.Dbref(1)).Select(v => Keys.ReadStr(v)).ToArray());

			await Assert.That(held).Contains("SEND_OOB")
				.Because("the grant has to follow the rename, as it does in PennMUSH");
			await Assert.That(held).DoesNotContain("PUEBLO_SEND")
				.Because("leaving the old edge behind would double-count the power");

			var orphan = db.Store.Read(tx => tx.TryGet(Tables.Power, Keys.Upper("Pueblo_Send"), out _));
			await Assert.That(orphan).IsFalse()
				.Because("the superseded record is dropped, not left orphaned beside the new one");
		}
		finally
		{
			await db.DisposeAsync();
			if (Directory.Exists(path))
			{
				try
				{
					Directory.Delete(path, recursive: true);
				}
				catch (IOException)
				{
				}
			}
		}
	}
}
