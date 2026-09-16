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
	[Test]
	public async Task MigrateRefreshesExistingListenParentTypeRestrictions()
	{
		var path = Path.Combine(Path.GetTempPath(), "listen-parent-seed-" + Guid.NewGuid().ToString("N"));
		var db = Create(path);
		try
		{
			await db.Migrate();
			await db.Store.WriteAsync(tx => tx.Put(Tables.Flag, Keys.Upper("LISTEN_PARENT"), Codec.Serialize(
				new FlagRecord { Name = "LISTEN_PARENT", Symbol = "^", TypeRestrictions = ["PLAYER"], System = true })));
			await db.Migrate();
			var flag = db.Store.Read(tx => tx.TryGet(Tables.Flag, Keys.Upper("LISTEN_PARENT"), out var value)
				? Codec.Deserialize<FlagRecord>(value) : throw new InvalidOperationException("Missing LISTEN_PARENT seed"));
			await Assert.That(flag.TypeRestrictions).IsEquivalentTo(["PLAYER", "THING", "ROOM"]);
		}
		finally
		{
			await db.DisposeAsync();
			await FixtureDirectoryCleanup.DeleteAsync(path);
		}
	}
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
			await Assert.That(db.Store.Count(Tables.Flag)).IsEqualTo(64);
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
			await FixtureDirectoryCleanup.DeleteAsync(path);
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
			await FixtureDirectoryCleanup.DeleteAsync(path);
		}
	}

	/// <summary>
	/// A flag an administrator created with <c>@flag/add</c> is not the seed's to redefine. PennMUSH's
	/// own built-in add path refuses the same way: <c>add_flag_generic</c> (<c>src/flags.c:2252</c>)
	/// opens with a "Don't double-add" guard that returns <c>FLAG_EXISTS</c> and leaves the existing
	/// definition alone. The seed keeps overwriting the rows it owns — that is how a corrected
	/// definition such as MYOPIC splitting off MISTRUST reaches an existing world — and <c>System</c>
	/// is exactly the line between the two, since <c>@flag/add</c> never sets it.
	/// </summary>
	[Test]
	public async Task MigrateLeavesAUserCreatedFlagAlone()
	{
		var path = Path.Combine(Path.GetTempPath(), "user-flag-" + Guid.NewGuid().ToString("N"));
		var db = Create(path);

		try
		{
			await db.Migrate();

			// A world where an admin added UNINSPECTED themselves, as the shipped help once described it.
			await db.Store.WriteAsync(tx => tx.Put(Tables.Flag, Keys.Upper("UNINSPECTED"), Codec.Serialize(
				new FlagRecord
				{
					Name = "UNINSPECTED",
					Symbol = "u",
					SetPermissions = ["FLAG^WIZARD"],
					UnsetPermissions = ["FLAG^WIZARD"],
					TypeRestrictions = ["PLAYER", "THING", "ROOM", "EXIT"],
					System = false
				})));

			await db.Migrate();

			var flag = db.Store.Read(tx => tx.TryGet(Tables.Flag, Keys.Upper("UNINSPECTED"), out var value)
				? Codec.Deserialize<FlagRecord>(value) : throw new InvalidOperationException("Missing UNINSPECTED"));

			await Assert.That(flag.System).IsFalse()
				.Because("the seed must not take ownership of a row an administrator created");
			await Assert.That(flag.TypeRestrictions).IsEquivalentTo(["PLAYER", "THING", "ROOM", "EXIT"])
				.Because("narrowing it to ROOM would strand every player already holding the flag");
			await Assert.That(flag.SetPermissions).IsEquivalentTo(["FLAG^WIZARD"]);
		}
		finally
		{
			await db.DisposeAsync();
			await FixtureDirectoryCleanup.DeleteAsync(path);
		}
	}

	/// <summary>The same line for powers, which <c>@power/add</c> creates the same way.</summary>
	[Test]
	public async Task MigrateLeavesAUserCreatedPowerAlone()
	{
		var path = Path.Combine(Path.GetTempPath(), "user-power-" + Guid.NewGuid().ToString("N"));
		var db = Create(path);

		try
		{
			await db.Migrate();

			await db.Store.WriteAsync(tx => tx.Put(Tables.Power, Keys.Upper("Quotas"), Codec.Serialize(
				new PowerRecord
				{
					Name = "Quotas",
					Alias = "",
					Symbol = "",
					SetPermissions = ["FLAG^WIZARD"],
					UnsetPermissions = ["FLAG^WIZARD"],
					TypeRestrictions = [],
					System = false,
					Disabled = false
				})));

			await db.Migrate();

			var power = db.Store.Read(tx => tx.TryGet(Tables.Power, Keys.Upper("Quotas"), out var value)
				? Codec.Deserialize<PowerRecord>(value) : throw new InvalidOperationException("Missing Quotas"));

			await Assert.That(power.System).IsFalse();
			await Assert.That(power.SetPermissions).IsEquivalentTo(["FLAG^WIZARD"]);
		}
		finally
		{
			await db.DisposeAsync();
			await FixtureDirectoryCleanup.DeleteAsync(path);
		}
	}

	/// <summary>
	/// The rename above is PennMUSH rewriting <em>its own</em> FLAG struct (<c>src/flags.c:850-855</c>).
	/// It has nothing to say about a power an administrator created under that name, which
	/// <c>@power/add</c> leaves <c>System = false</c>. Moving its grants onto SEND_OOB and deleting the
	/// row would destroy exactly the definition the seed guard above refuses to overwrite.
	/// </summary>
	[Test]
	public async Task MigrateLeavesAnAdministratorsOwnPuebloSendAlone()
	{
		var path = Path.Combine(Path.GetTempPath(), "user-pueblo-" + Guid.NewGuid().ToString("N"));
		var db = Create(path);

		try
		{
			await db.Migrate();

			await db.Store.WriteAsync(tx =>
			{
				tx.Put(Tables.Power, Keys.Upper("Pueblo_Send"), Codec.Serialize(new PowerRecord
				{
					Name = "Pueblo_Send",
					Alias = "",
					Symbol = "",
					SetPermissions = ["FLAG^WIZARD"],
					UnsetPermissions = ["FLAG^WIZARD"],
					TypeRestrictions = [],
					System = false,
					Disabled = false
				}));
				tx.Put(Tables.ObjPower.Forward, Keys.Dbref(1), Keys.Upper("Pueblo_Send"));
				tx.Put(Tables.ObjPower.Reverse, Keys.Upper("Pueblo_Send"), Keys.Dbref(1));
				return true;
			});

			await db.Migrate();

			var power = db.Store.Read(tx => tx.TryGet(Tables.Power, Keys.Upper("Pueblo_Send"), out var value)
				? Codec.Deserialize<PowerRecord>(value) : null);

			await Assert.That(power).IsNotNull()
				.Because("an administrator's own power is not the rename's to delete");
			await Assert.That(power!.System).IsFalse();

			var held = db.Store.Read(tx =>
				tx.Dups(Tables.ObjPower.Forward, Keys.Dbref(1)).Select(v => Keys.ReadStr(v)).ToArray());

			await Assert.That(held).Contains("PUEBLO_SEND")
				.Because("the grant belongs to the administrator's power, not to SEND_OOB");
		}
		finally
		{
			await db.DisposeAsync();
			await FixtureDirectoryCleanup.DeleteAsync(path);
		}
	}

}
