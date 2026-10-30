using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.World.Common;
using SharpMUSH.Plugins.World.Storage;

namespace SharpMUSH.Tests.WorldPlugin;

/// <summary>The records of the rooms standing on a stored map: lent, placed to stay, let go, and waiting as spares.</summary>
public class WorldRoomStoreTests
{
	private string _path = null!;
	private LightningDatabase _db = null!;
	private WorldStore _store = null!;
	private static readonly DBRef Map = new(4400, 1000);
	private static readonly DBRef Lent = new(4401, 2000);
	private static readonly DBRef Kept = new(4402, 3000);

	[Before(Test)]
	public async Task Open()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-world-rooms-" + Guid.NewGuid().ToString("N"));
		_db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		await _db.Migrate();
		_store = new WorldStore(_db);
		await _store.CreateAsync(Map, "square");
		await _store.PaintAsync(Map, 0, 0, 0, 3, 3, "grass");
	}

	[After(Test)]
	public async Task Close()
	{
		await _db.DisposeAsync();
		try
		{
			if (Directory.Exists(_path)) Directory.Delete(_path, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort: a lingering mdb.lck can outlive the writer join.
		}
	}

	[Test]
	public async Task APlaceIsLentOneRoomAndASecondLendGetsTheFirst()
	{
		await Assert.That(await _store.LendAsync(Map, 1, 1, 0, Lent)).IsEqualTo(Lent);
		await Assert.That(await _store.LendAsync(Map, 1, 1, 0, Kept)).IsEqualTo(Lent);
		await Assert.That(_store.StandingAt(Map, 1, 1, 0)).IsEqualTo(Lent);
		var record = _store.Room(Lent)!;
		await Assert.That((record.X, record.Y, record.Z, record.Permanent)).IsEqualTo((1, 1, 0, false));
		await Assert.That(_store.Room(Kept)).IsNull();
		await Assert.That(_store.Room(new DBRef(Lent.Number, 1))).IsNull().Because("another object with the room's number stands nowhere");
		await Assert.That(await _store.LendAsync(new DBRef(Map.Number, 5), 1, 1, 0, Lent)).IsNull();
	}

	[Test]
	public async Task PlacingARoomKeepsALentOneOrTakesOverFromIt()
	{
		await _store.LendAsync(Map, 1, 1, 0, Lent);
		await Assert.That(await _store.PlaceRoomAsync(Map, 1, 1, 0, Lent)).IsEqualTo(WorldPlacing.Kept);
		await Assert.That(_store.Room(Lent)!.Permanent).IsTrue();
		await Assert.That(await _store.PlaceRoomAsync(Map, 1, 1, 0, Lent)).IsEqualTo(WorldPlacing.AlreadyThere);
		await Assert.That(await _store.PlaceRoomAsync(Map, 1, 1, 0, Kept)).IsEqualTo(WorldPlacing.Taken);

		await _store.LendAsync(Map, 2, 2, 0, Kept);
		var other = new DBRef(4403, 4000);
		await Assert.That(await _store.PlaceRoomAsync(Map, 2, 2, 0, other)).IsEqualTo(WorldPlacing.Placed);
		await Assert.That(_store.StandingAt(Map, 2, 2, 0)).IsEqualTo(other);
		await Assert.That(_store.Room(Kept)).IsNull();
		await Assert.That(_store.Releases()).IsEquivalentTo([(Kept, new WorldReleaseRecord(Kept.CreationMilliseconds!.Value, true))]);

		// A room placed somewhere else moves, and its old place is free again.
		await _store.PlaceRoomAsync(Map, 3, 3, 0, other);
		await Assert.That(_store.StandingAt(Map, 2, 2, 0)).IsNull();
		await Assert.That(_store.StandingAt(Map, 3, 3, 0)).IsEqualTo(other);
	}

	[Test]
	public async Task ARoomLetGoWaitsToBeTidiedAndThenAsASpare()
	{
		await _store.LendAsync(Map, 1, 1, 0, Lent);
		await _store.SetEmptySinceAsync(Lent, 50);
		await Assert.That(_store.Room(Lent)!.EmptySince).IsEqualTo(50L);

		await Assert.That((await _store.UnstandAsync(Lent, release: true))?.X).IsEqualTo(1);
		await Assert.That(_store.StandingAt(Map, 1, 1, 0)).IsNull();
		await Assert.That(_store.Releases().Select(release => release.Room)).IsEquivalentTo([Lent]);

		await _store.DropReleaseAsync(Lent);
		var builder = new DBRef(10);
		var stranger = new DBRef(11);
		var strangers = new DBRef(4403, 4000);
		await _store.AddSpareAsync(Kept, builder, 20);
		await _store.AddSpareAsync(Lent, builder, 10);
		await _store.AddSpareAsync(strangers, stranger, 5);
		await Assert.That(_store.Spares().Select(spare => spare.Room).ToList()).IsEquivalentTo([strangers, Lent, Kept]);
		await Assert.That(_store.Spares()[0].Room).IsEqualTo(strangers);
		await Assert.That(await _store.TakeSpareAsync(builder)).IsEqualTo(Lent).Because("a spare is lent again only to its owner's maps");
		await Assert.That(await _store.TakeSpareAsync(builder)).IsEqualTo(Kept);
		await Assert.That(await _store.TakeSpareAsync(builder)).IsNull();
		await Assert.That(await _store.TakeSpareAsync(stranger)).IsEqualTo(strangers);
	}

	[Test]
	public async Task AFreedMapLetsGoOfItsRoomsAndAFreedRoomIsForgotten()
	{
		await _store.LendAsync(Map, 1, 1, 0, Lent);
		await _store.PlaceRoomAsync(Map, 2, 2, 0, Kept);
		await _store.ObjectsFreedAsync([Map]);
		await Assert.That(_store.Rooms()).IsEmpty();
		await Assert.That(_store.Releases()).IsEquivalentTo(
		[
			(Lent, new WorldReleaseRecord(Lent.CreationMilliseconds!.Value, true)),
			(Kept, new WorldReleaseRecord(Kept.CreationMilliseconds!.Value, false)),
		]);

		await _store.AddSpareAsync(Lent, new DBRef(10), 1);
		await _store.ObjectsFreedAsync([Lent, new DBRef(Kept.Number, 1)]);
		await Assert.That(_store.Spares()).IsEmpty();
		await Assert.That(_store.Releases().Select(release => release.Room)).IsEquivalentTo([Kept]).Because("a later object with its number was freed, not it");
	}

	[Test]
	public async Task SparesPastTheKeepOrWaitingAWeekAreDestroyedOldestFirst()
	{
		var week = (long)WorldRooms.SpareMaxIdle.TotalMilliseconds;
		var spares = Enumerable.Range(0, WorldRooms.SpareKeep + 2)
			.Select(i => (new DBRef(5000 + i, i), new WorldSpareRecord(i, week + i)))
			.ToList();
		await Assert.That(WorldRooms.Surplus(spares, week + 10)).IsEquivalentTo([new DBRef(5000, 0), new DBRef(5001, 1)]);
		await Assert.That(WorldRooms.Surplus(spares[..3], 2 * week + 1)).IsEquivalentTo([new DBRef(5000, 0), new DBRef(5001, 1)]);
		await Assert.That(WorldRooms.Surplus(spares[2..4], 2 * week + 1)).IsEmpty();
	}
}
