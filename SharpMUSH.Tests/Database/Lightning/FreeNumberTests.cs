using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// The free numbers: a purged recover entry's number comes back to a creation that names none only once it is due,
/// the earliest due first, never while protected, and at once to one that asks for it.
/// </summary>
public class FreeNumberTests
{
	private string _path = null!;
	private LightningDatabase _db = null!;
	private SharpPlayer _god = null!;

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Join(Path.GetTempPath(), "free-numbers-" + Guid.NewGuid().ToString("N"));
		_db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		await _db.Migrate();
		_god = (await _db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		await FixtureDirectoryCleanup.DeleteAsync(_path);
	}

	private async Task<DBRef> RoomAsync(string name) => await _db.CreateRoomAsync(name, _god);

	/// <summary>Creates a room, retires it onto the recover list and purges the entry, due at <paramref name="reusableAt"/>.</summary>
	private async Task<int> FreedAsync(string name, DateTimeOffset? reusableAt)
	{
		var room = await RoomAsync(name);
		await Assert.That(await _db.RetireObjectAsync(room, null)).IsTrue();
		await Assert.That(await _db.PurgeRecoverEntryAsync(room, false, reusableAt)).IsTrue();
		return room.Number;
	}

	[Test]
	public async Task ANumberNotDueYetIsOnlyGivenToACreationThatAsksForIt()
	{
		var held = await FreedAsync("Early", DateTimeOffset.UtcNow.AddDays(1));

		var unasked = await RoomAsync("Unasked");
		await Assert.That(unasked.Number).IsNotEqualTo(held);
		await Assert.That(await _db.IsFreeNumberAsync(held)).IsTrue();
		await Assert.That(await _db.GetNextDbrefAsync()).IsEqualTo(unasked.Number + 1);

		var asked = (await _db.CreateRoomAtAsync(new DBRef(held), "Asked", _god)).Expect<DBRef>();
		await Assert.That(asked.Number).IsEqualTo(held);
		await Assert.That(await _db.IsFreeNumberAsync(held)).IsFalse();
	}

	[Test]
	public async Task DueNumbersComeBackEarliestDueFirstThenTheCounter()
	{
		var now = DateTimeOffset.UtcNow;
		var laterRoom = await RoomAsync("DueLater");
		var earlierRoom = await RoomAsync("DueEarlier");
		foreach (var (room, due) in new[] { (laterRoom, now.AddHours(-1)), (earlierRoom, now.AddHours(-2)) })
		{
			await _db.RetireObjectAsync(room, null);
			await _db.PurgeRecoverEntryAsync(room, false, due);
		}

		var (later, earlier) = (laterRoom.Number, earlierRoom.Number);
		var counter = await _db.GetNextDbrefAsync();

		await Assert.That(counter).IsEqualTo(earlier).Because("nextdbref() names what the next creation takes");
		await Assert.That((await RoomAsync("First")).Number).IsEqualTo(earlier);
		await Assert.That((await RoomAsync("Second")).Number).IsEqualTo(later);
		await Assert.That(await _db.CountFreeNumbersAsync()).IsEqualTo(0);
		await Assert.That((await RoomAsync("Third")).Number).IsGreaterThan(Math.Max(earlier, later));
	}

	[Test]
	public async Task AProtectedNumberIsNeverTakenUnasked()
	{
		var named = await FreedAsync("Handler", DateTimeOffset.UtcNow.AddHours(-1));
		await _db.SetProtectedNumbersAsync([named]);

		await Assert.That((await RoomAsync("Elsewhere")).Number).IsNotEqualTo(named);
		await Assert.That(await _db.IsFreeNumberAsync(named)).IsTrue();

		await _db.SetProtectedNumbersAsync([]);
		await Assert.That((await RoomAsync("Now")).Number).IsEqualTo(named);
	}

	[Test]
	public async Task ANumberHeldForeverIsListedWithoutADueTime()
	{
		var kept = await FreedAsync("Character", null);

		var listed = await _db.GetFreeNumbersAsync().SingleAsync(free => free.Number == kept);
		await Assert.That(listed.ReusableAt).IsNull();
		await Assert.That((await RoomAsync("Other")).Number).IsNotEqualTo(kept);
	}

	[Test]
	public async Task UntrackedHolesAreRecordedOnceWithTheirDueTime()
	{
		var hole = await FreedAsync("Hole", DateTimeOffset.UtcNow.AddHours(-1));
		await _db.Store.WriteAsync(tx => LightningDatabase.ForgetFreeNumber(tx, hole));
		await Assert.That(await _db.IsFreeNumberAsync(hole)).IsFalse();

		var due = DateTimeOffset.UtcNow.AddDays(30);
		await Assert.That(await _db.RecordUntrackedHolesAsync(due)).IsEqualTo(1);
		await Assert.That(await _db.RecordUntrackedHolesAsync(due)).IsEqualTo(0);
		var listed = await _db.GetFreeNumbersAsync().SingleAsync(free => free.Number == hole);
		await Assert.That(listed.ReusableAt?.ToUnixTimeMilliseconds()).IsEqualTo(due.ToUnixTimeMilliseconds());
	}

	[Test]
	public async Task ARecoverEntryKeepsItsNumberOutOfTheFreeNumbers()
	{
		var room = await RoomAsync("Retired");
		await _db.RetireObjectAsync(room, null);

		await Assert.That(await _db.IsFreeNumberAsync(room.Number)).IsFalse();
		await Assert.That(await _db.IsDbrefAvailableAsync(new DBRef(room.Number))).IsFalse();
		await Assert.That((await RoomAsync("Next")).Number).IsNotEqualTo(room.Number);
	}

	[Test]
	public async Task ReleasingTrailingNumbersForgetsTheFreeOnesAboveTheCounter()
	{
		var top = await FreedAsync("Top", DateTimeOffset.UtcNow.AddHours(-1));

		var next = await _db.ReleaseTrailingDbrefsAsync();

		await Assert.That(next).IsLessThanOrEqualTo(top);
		await Assert.That(await _db.IsFreeNumberAsync(top)).IsFalse();
	}

	[Test]
	public async Task ReferenceSearchFindsBareNumbersButNotObjids()
	{
		var holder = await RoomAsync("Holder");
		await _db.SetAttributeAsync(holder, ["CODE"], MarkupText.Plain("[pemit(#4242,hi)] [u(#4243:1789000000000/fn)] #42420 ##4244"), _god);

		var found = await _db.FindNumberReferencesAsync(new HashSet<int> { 4242, 4243, 4244 }).ToListAsync();

		await Assert.That(found.Select(reference => (reference.Holder, reference.Attribute, reference.Number)))
			.IsEquivalentTo([(holder.Number, "CODE", 4242)]);
	}
}
