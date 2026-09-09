using System.Text.Json;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using LightningProvider = SharpMUSH.Database.Lightning.LightningDatabase;
using SurrealProvider = SharpMUSH.Database.SurrealDB.SurrealDatabase;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// A world written before lock names were canonical can hold two spellings of one lock on one
/// object — <c>@CHZONE</c> wrote its default under <c>ChZone</c> while <c>@lock/chzone</c> wrote the
/// player's under <c>Chzone</c>. Both providers now key their lock maps case-insensitively, and that
/// row is exactly the one a naive comparer change throws a duplicate-key exception on, at world
/// load. These run the real load path of each provider against it.
/// </summary>
public class LegacyLockNameLoadTests
{
	private static LockRecord Stored(string lockString) => new() { LockString = lockString, Flags = "" };

	/// <summary>The stored shape SurrealDB's <c>object.locks</c> column holds.</summary>
	private static string SurrealJson(params (string Name, string LockString)[] locks)
		=> JsonSerializer.Serialize(locks.ToDictionary(
			entry => entry.Name,
			entry => new
			{
				entry.LockString,
				Flags = ""
			}));

	[Test]
	public async Task LightningLoadsAnObjectCarryingBothSpellingsOfOneLock()
	{
		var loaded = LightningProvider.MapLocks(new Dictionary<string, LockRecord>
		{
			["ChZone"] = Stored("=#11"),
			["Chzone"] = Stored("=#12"),
			["tport"] = Stored("=#13"),
			["Basic"] = Stored("=#14")
		});

		await AssertFoldedTheLegacyRow(loaded);
	}

	[Test]
	public async Task SurrealLoadsAnObjectCarryingBothSpellingsOfOneLock()
	{
		var loaded = SurrealProvider.DeserializeLocks(SurrealJson(
			("ChZone", "=#11"),
			("Chzone", "=#12"),
			("tport", "=#13"),
			("Basic", "=#14")));

		await AssertFoldedTheLegacyRow(loaded);
	}

	private static async Task AssertFoldedTheLegacyRow(
		System.Collections.Immutable.IImmutableDictionary<string, SharpLockData> loaded)
	{
		// One entry per lock, under the spelling the gates read.
		await Assert.That(loaded.Count).IsEqualTo(3);

		// The canonically-spelled entry wins: it is the only one the gate could already see, so
		// loading an old world must not change a permission decision it is already enforcing.
		await Assert.That(loaded[nameof(LockType.ChZone)].LockString).IsEqualTo("=#11");

		// A lock stored only under the legacy spelling moves to the canonical one, which is the
		// whole point of the fix — it was invisible to the gate before.
		await Assert.That(loaded[nameof(LockType.Teleport)].LockString).IsEqualTo("=#13");
		await Assert.That(loaded[nameof(LockType.Basic)].LockString).IsEqualTo("=#14");

		// Case-insensitive from here on, so the old spelling still resolves for a reader.
		await Assert.That(loaded.ContainsKey("Chzone")).IsTrue();
		await Assert.That(loaded.ContainsKey("CHZONE")).IsTrue();
		// "tport" is a different word, not a case variation, so it is gone as a key.
		await Assert.That(loaded.ContainsKey("tport")).IsFalse();
	}

	[Test]
	public async Task BothProvidersAgreeOnWhichEntryTheFoldKeeps()
	{
		var lightning = LightningProvider.MapLocks(new Dictionary<string, LockRecord>
		{
			["Chzone"] = Stored("=#12"),
			["CHZONE"] = Stored("=#13"),
			["chzone"] = Stored("=#14")
		});
		var surreal = SurrealProvider.DeserializeLocks(
			SurrealJson(("Chzone", "=#12"), ("CHZONE", "=#13"), ("chzone", "=#14")));

		// No canonically-spelled entry present, so the ordinally-first key wins — arbitrary, but the
		// same arbitrary answer in both providers and on every load, which hash order is not.
		await Assert.That(lightning.Count).IsEqualTo(1);
		await Assert.That(lightning[nameof(LockType.ChZone)].LockString).IsEqualTo("=#13");
		await Assert.That(surreal.Count).IsEqualTo(1);
		await Assert.That(surreal[nameof(LockType.ChZone)].LockString).IsEqualTo("=#13");
	}

	[Test]
	public async Task LightningRecordStillRoundTripsBothSpellings()
	{
		// The persisted record stays an ordinal dictionary on purpose: deserialising a legacy row
		// into a case-insensitive one would throw before the fold ever got to run.
		var record = new ObjectRecord
		{
			Name = "Zone",
			Type = "THING",
			CreationTime = 1,
			ModifiedTime = 1,
			Locks = new Dictionary<string, LockRecord>
			{
				["ChZone"] = Stored("=#11"),
				["Chzone"] = Stored("=#12")
			}
		};

		var back = Codec.Deserialize<ObjectRecord>(Codec.Serialize(record));

		await Assert.That(back.Locks.Count).IsEqualTo(2);
		await Assert.That(LightningProvider.MapLocks(back.Locks).Count).IsEqualTo(1);
	}

	[Test]
	[Arguments("chzone", nameof(LockType.ChZone))]
	[Arguments("CHZONE", nameof(LockType.ChZone))]
	[Arguments("Teleport", nameof(LockType.Teleport))]
	[Arguments("tport", nameof(LockType.Teleport))]
	[Arguments("dropto", nameof(LockType.DropTo))]
	[Arguments("chown", nameof(LockType.ChOwn))]
	[Arguments("use", nameof(LockType.Use))]
	[Arguments("mailforward", nameof(LockType.MailForward))]
	public async Task CanonicalResolvesEverySpellingALockCanArriveIn(string given, string expected)
		=> await Assert.That(LockNames.Canonical(given)).IsEqualTo(expected);

	[Test]
	[Arguments("Whatever")]
	[Arguments("User:MyLock")]
	public async Task CanonicalLeavesANonStandardLockAlone(string given)
		=> await Assert.That(LockNames.Canonical(given)).IsEqualTo(given);

	[Test]
	public async Task EverySystemLockIsSpelledExactlyAsItsLockTypeMember()
	{
		// The bug this guards: SystemLocks' keys are what @lock stores under and LockType is what
		// every gate looks up, so a second list of spellings is a permission hole waiting to happen.
		var systemLocks = new Library.Services.LockService(null!, null!).SystemLocks;

		await Assert.That(systemLocks.Count).IsEqualTo(Enum.GetNames<LockType>().Length);
		foreach (var name in Enum.GetNames<LockType>())
		{
			await Assert.That(systemLocks.Keys.Contains(name, StringComparer.Ordinal)).IsTrue();
		}
	}
}
