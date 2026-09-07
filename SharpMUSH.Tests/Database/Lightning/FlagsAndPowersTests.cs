using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

public class FlagsAndPowersTests
{
	// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private LightningDatabase _db = null!;

	[Before(Test)]
	public async Task Setup()
	{
		var path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_db = Create(path);
		await _db.Migrate();
	}

	[Test]
	public async Task GetObjectFlagAsync_MatchesAliasCaseInsensitively()
	{
		// COLOR is seeded with alias COLOUR (FlagSeed.Flags); ask for it lowercase and misspelled-case.
		var byAlias = await _db.GetObjectFlagAsync("colour");

		await Assert.That(byAlias).IsNotNull();
		await Assert.That(byAlias!.Name).IsEqualTo("COLOR");
		await Assert.That(byAlias.Aliases ?? []).Contains("COLOUR");
	}

	[Test]
	public async Task GetObjectFlagAsync_MatchesNameCaseInsensitively()
	{
		var flag = await _db.GetObjectFlagAsync("wizard");

		await Assert.That(flag).IsNotNull();
		await Assert.That(flag!.Name).IsEqualTo("WIZARD");
		await Assert.That(flag.System).IsTrue();
	}

	[Test]
	public async Task GetObjectFlagAsync_UnknownNameReturnsNull()
		=> await Assert.That(await _db.GetObjectFlagAsync("NO_SUCH_FLAG_AT_ALL")).IsNull();

	[Test]
	public async Task SetAndUnsetObjectFlag_WriteBothEdgeDirections()
	{
		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known;
		var dark = (await _db.GetObjectFlagAsync("DARK"))!;
		var dbref = (long)god.Object().Key;

		var set = await _db.SetObjectFlagAsync(god, dark);
		await Assert.That(set).IsTrue();

		var forward = _db.Store.Read(tx => tx.Dups(Tables.ObjFlag.Forward, Keys.Dbref(dbref))
			.Select(v => Keys.ReadStr(v)).ToList());
		var reverse = _db.Store.Read(tx => tx.Dups(Tables.ObjFlag.Reverse, Keys.Upper("DARK"))
			.Select(v => Keys.ReadDbref(v)).ToList());

		await Assert.That(forward).Contains("DARK");
		await Assert.That(reverse).Contains(dbref);

		// Setting an already-set flag is refused, same as SurrealDatabase.
		await Assert.That(await _db.SetObjectFlagAsync(god, dark)).IsFalse();

		var unset = await _db.UnsetObjectFlagAsync(god, dark);
		await Assert.That(unset).IsTrue();

		var forwardAfter = _db.Store.Read(tx => tx.Dups(Tables.ObjFlag.Forward, Keys.Dbref(dbref))
			.Select(v => Keys.ReadStr(v)).ToList());
		var reverseAfter = _db.Store.Read(tx => tx.Dups(Tables.ObjFlag.Reverse, Keys.Upper("DARK"))
			.Select(v => Keys.ReadDbref(v)).ToList());

		await Assert.That(forwardAfter).DoesNotContain("DARK");
		await Assert.That(reverseAfter).DoesNotContain(dbref);

		// Unsetting an already-unset flag is refused.
		await Assert.That(await _db.UnsetObjectFlagAsync(god, dark)).IsFalse();
	}

	[Test]
	public async Task SetObjectFlagDisabledAsync_RefusesSystemFlags()
	{
		var result = await _db.SetObjectFlagDisabledAsync("WIZARD", true);

		await Assert.That(result).IsFalse();
		await Assert.That((await _db.GetObjectFlagAsync("WIZARD"))!.Disabled).IsFalse();
	}

	[Test]
	public async Task SetPowerDisabledAsync_RefusesSystemPowers()
	{
		var seeded = await _db.GetObjectPowersAsync().FirstOrDefaultAsync();
		await Assert.That(seeded).IsNotNull().Because("PowerSeed must seed at least one system power for this to test anything");

		var result = await _db.SetPowerDisabledAsync(seeded!.Name, true);

		await Assert.That(result).IsFalse();
		await Assert.That((await _db.GetPowerAsync(seeded.Name))!.Disabled).IsFalse();
	}

	[Test]
	public async Task CreateAndDeleteCustomFlag_RoundTrips()
	{
		const string name = "TEST_CUSTOM_FLAG";
		var created = await _db.CreateObjectFlagAsync(name, ["TCF"], "T", false,
			["FLAG^WIZARD"], ["FLAG^WIZARD"], ["PLAYER", "THING", "ROOM", "EXIT"]);

		await Assert.That(created).IsNotNull();
		await Assert.That(created!.System).IsFalse();

		var reread = await _db.GetObjectFlagAsync(name);
		await Assert.That(reread).IsNotNull();
		await Assert.That(reread!.Aliases ?? []).Contains("TCF");

		var deleted = await _db.DeleteObjectFlagAsync(name);
		await Assert.That(deleted).IsTrue();
		await Assert.That(await _db.GetObjectFlagAsync(name)).IsNull();
	}

	[Test]
	public async Task DeleteObjectFlagAsync_RefusesSystemFlagsAndDoesNotDeleteThem()
	{
		var deleted = await _db.DeleteObjectFlagAsync("WIZARD");

		await Assert.That(deleted).IsFalse();
		await Assert.That(await _db.GetObjectFlagAsync("WIZARD")).IsNotNull();
	}

	[Test]
	public async Task DeleteObjectFlagAsync_RemovesItsObjectEdges()
	{
		const string name = "TEST_FLAG_WITH_EDGE";
		await _db.CreateObjectFlagAsync(name, null, "T", false, [], [], ["PLAYER", "THING", "ROOM", "EXIT"]);
		var flag = (await _db.GetObjectFlagAsync(name))!;
		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known;
		var dbref = (long)god.Object().Key;

		await Assert.That(await _db.SetObjectFlagAsync(god, flag)).IsTrue();
		await Assert.That(await _db.DeleteObjectFlagAsync(name)).IsTrue();

		var leftovers = _db.Store.Read(tx =>
			tx.Dups(Tables.ObjFlag.Forward, Keys.Dbref(dbref)).Select(v => Keys.ReadStr(v)).Count(n => n == name)
			+ tx.Dups(Tables.ObjFlag.Reverse, Keys.Upper(name)).Count());
		await Assert.That(leftovers).IsEqualTo(0);
	}

	[Test]
	public async Task SetAndUnsetObjectPower_WriteBothEdgeDirections()
	{
		const string name = "TEST_CUSTOM_POWER";
		var power = await _db.CreatePowerAsync(name, "TCP", "", false, [], [], ["PLAYER"]);
		await Assert.That(power).IsNotNull();

		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known;
		var dbref = (long)god.Object().Key;

		await Assert.That(await _db.SetObjectPowerAsync(god, power!)).IsTrue();

		var forward = _db.Store.Read(tx => tx.Dups(Tables.ObjPower.Forward, Keys.Dbref(dbref))
			.Select(v => Keys.ReadStr(v)).ToList());
		var reverse = _db.Store.Read(tx => tx.Dups(Tables.ObjPower.Reverse, Keys.Upper(name))
			.Select(v => Keys.ReadDbref(v)).ToList());

		await Assert.That(forward).Contains(name);
		await Assert.That(reverse).Contains(dbref);

		// Setting an already-set power is refused.
		await Assert.That(await _db.SetObjectPowerAsync(god, power!)).IsFalse();

		await Assert.That(await _db.UnsetObjectPowerAsync(god, power!)).IsTrue();

		var forwardAfter = _db.Store.Read(tx => tx.Dups(Tables.ObjPower.Forward, Keys.Dbref(dbref))
			.Select(v => Keys.ReadStr(v)).ToList());
		await Assert.That(forwardAfter).DoesNotContain(name);

		// Unsetting an already-unset power is refused.
		await Assert.That(await _db.UnsetObjectPowerAsync(god, power!)).IsFalse();
	}

	[After(Test)]
	public async Task Cleanup()
	{
		var path = _db.Store.Path;
		await _db.DisposeAsync();
		if (Directory.Exists(path))
		{
			try
			{
				Directory.Delete(path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort: see MigrationTests for why this can outlive the writer thread's join.
			}
		}
	}
}
