using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// <see cref="INavigationStore"/> ported onto the LMDB edge tables: locations, contents, exits,
/// entrances, homed-at, parent chains and reachability. Semantics mirror
/// <c>SurrealDatabase.Navigation.cs</c> / <c>SurrealDatabase.Objects.cs</c>.
/// </summary>
public class NavigationTests
{
	// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private LightningDatabase _db = null!;
	private string _path = null!;

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_db = Create(_path);
		await _db.Migrate();
	}

	[After(Test)]
	public Task Cleanup()
	{
		_db.Store.Dispose();
		if (Directory.Exists(_path))
		{
			try
			{
				Directory.Delete(_path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort, same as MigrationTests: a lingering mdb.lck can outlive the writer join.
			}
		}

		return Task.CompletedTask;
	}

	[Test]
	public async Task ReachabilityFollowsParentsAndZonesWithoutLooping()
	{
		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known.AsPlayer;
		var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Known.AsContainer;
		var aRef = await _db.CreateThingAsync("A", room, god, room);
		var bRef = await _db.CreateThingAsync("B", room, god, room);
		var cRef = await _db.CreateThingAsync("C", room, god, room);

		var a = (await _db.GetObjectNodeAsync(aRef)).Known;
		var b = (await _db.GetObjectNodeAsync(bRef)).Known;
		var c = (await _db.GetObjectNodeAsync(cRef)).Known;

		await _db.SetObjectParent(a, b);
		await _db.SetObjectZone(b, c);

		await Assert.That(await _db.IsReachableViaParentOrZoneAsync(a, c, 100)).IsTrue();
		await Assert.That(await _db.IsReachableViaParentOrZoneAsync(c, a, 100)).IsFalse();

		await _db.SetObjectParent(c, a);
		await Assert.That(await _db.IsReachableViaParentOrZoneAsync(a, a, 100)).IsTrue();
	}

	[Test]
	public async Task ReachabilityFindsTargetThroughZoneWhenParentIsADeadEnd()
	{
		// A.parent = X (dead end), A.zone = Y, Y.parent = Z (target). A single-path walk that
		// follows parent-over-zone from A dead-ends at X and misses Z; a true BFS over both
		// edges from every node still finds it via A -> zone Y -> parent Z.
		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known.AsPlayer;
		var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Known.AsContainer;

		var aRef = await _db.CreateThingAsync("A", room, god, room);
		var xRef = await _db.CreateThingAsync("X", room, god, room);
		var yRef = await _db.CreateThingAsync("Y", room, god, room);
		var zRef = await _db.CreateThingAsync("Z", room, god, room);

		var a = (await _db.GetObjectNodeAsync(aRef)).Known;
		var x = (await _db.GetObjectNodeAsync(xRef)).Known;
		var y = (await _db.GetObjectNodeAsync(yRef)).Known;
		var z = (await _db.GetObjectNodeAsync(zRef)).Known;

		await _db.SetObjectParent(a, x);
		await _db.SetObjectZone(a, y);
		await _db.SetObjectParent(y, z);

		await Assert.That(await _db.IsReachableViaParentOrZoneAsync(a, z, 100)).IsTrue();
	}

	[Test]
	public async Task ReachabilityFindsTargetThroughParentWhenZoneIsADeadEnd()
	{
		// Mirrored: A.zone = X (dead end), A.parent = Y, Y.zone = Z (target).
		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known.AsPlayer;
		var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Known.AsContainer;

		var aRef = await _db.CreateThingAsync("A", room, god, room);
		var xRef = await _db.CreateThingAsync("X", room, god, room);
		var yRef = await _db.CreateThingAsync("Y", room, god, room);
		var zRef = await _db.CreateThingAsync("Z", room, god, room);

		var a = (await _db.GetObjectNodeAsync(aRef)).Known;
		var x = (await _db.GetObjectNodeAsync(xRef)).Known;
		var y = (await _db.GetObjectNodeAsync(yRef)).Known;
		var z = (await _db.GetObjectNodeAsync(zRef)).Known;

		await _db.SetObjectZone(a, x);
		await _db.SetObjectParent(a, y);
		await _db.SetObjectZone(y, z);

		await Assert.That(await _db.IsReachableViaParentOrZoneAsync(a, z, 100)).IsTrue();
	}

	[Test]
	public async Task ContentsAndExitsListWhatWasCreatedInARoom()
	{
		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known.AsPlayer;
		var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Known.AsContainer;

		var thingRef = await _db.CreateThingAsync("Gadget", room, god, room);
		var exitRef = await _db.CreateExitAsync("Out", ["O"], room, god);

		var contentKeys = (await _db.GetContentsAsync(room).ToListAsync())
			.Select(c => c.Object().DBRef.Number).ToList();
		await Assert.That(contentKeys).Contains(thingRef.Number);
		await Assert.That(contentKeys).Contains(exitRef.Number);

		var exitKeys = (await _db.GetExitsAsync(room).ToListAsync())
			.Select(e => e.Object.DBRef.Number).ToList();
		await Assert.That(exitKeys).Contains(exitRef.Number);
		await Assert.That(exitKeys).DoesNotContain(thingRef.Number);
	}

	[Test]
	public async Task EntrancesListsExitsLinkedToTheDestination()
	{
		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known.AsPlayer;
		var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Known.AsContainer;
		var destinationRef = await _db.CreateRoomAsync("Destination", god);
		var destination = (await _db.GetObjectNodeAsync(destinationRef)).Known.AsContainer;

		var exitRef = await _db.CreateExitAsync("Out", ["O"], room, god);
		var exit = (await _db.GetObjectNodeAsync(exitRef)).Known.AsExit;
		await _db.LinkExitAsync(exit, destination);

		var entranceKeys = (await _db.GetEntrancesAsync(destinationRef).ToListAsync())
			.Select(e => e.Object.DBRef.Number).ToList();
		await Assert.That(entranceKeys).Contains(exitRef.Number);
	}

	[Test]
	public async Task HomedAtExcludesRoomsButIncludesThingsAndExits()
	{
		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known.AsPlayer;
		var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Known.AsContainer;

		var homeRef = await _db.CreateRoomAsync("Home Base", god);
		var home = (await _db.GetObjectNodeAsync(homeRef)).Known.AsContainer;

		var thingRef = await _db.CreateThingAsync("Wanderer", room, god, home);

		// An exit's destination reuses the same home edge (Task 7's LinkExitAsync), same as a
		// room's drop-to — it must show up here, unlike the room case below.
		var exitRef = await _db.CreateExitAsync("Out", ["O"], room, god);
		var exit = (await _db.GetObjectNodeAsync(exitRef)).Known.AsExit;
		await _db.LinkExitAsync(exit, home);

		// A room's own drop-to also reuses the home edge — must not show up as "homed at" the target.
		var dropRoomRef = await _db.CreateRoomAsync("Drops Here", god);
		var dropRoom = (await _db.GetObjectNodeAsync(dropRoomRef)).Known.AsRoom;
		await _db.LinkRoomAsync(dropRoom, home.WithNoneOption());

		var homedKeys = (await _db.GetHomedAtAsync(homeRef).ToListAsync())
			.Select(c => c.Object().DBRef.Number).ToList();
		await Assert.That(homedKeys).Contains(thingRef.Number);
		await Assert.That(homedKeys).Contains(exitRef.Number);
		await Assert.That(homedKeys).DoesNotContain(dropRoomRef.Number);
	}

	[Test]
	public async Task LocationDepthNegativeOneWalksToTheTop()
	{
		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known.AsPlayer;
		var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Known.AsContainer;

		var boxRef = await _db.CreateThingAsync("Box", room, god, room);
		var boxNode = (await _db.GetObjectNodeAsync(boxRef)).Known;
		var coinRef = await _db.CreateThingAsync("Coin", room, god, room);
		var coin = (await _db.GetObjectNodeAsync(coinRef)).Known.AsContent;

		// Coin sits inside the box, which sits in the room: two hops to the room.
		await _db.SetContentLocation(coin, boxNode.AsContainer);
		await _db.SetContentLocation(boxNode.AsContent, room);

		var top = await _db.GetLocationAsync(coinRef, -1);
		await Assert.That(top.WithoutNone().Object().DBRef.Number).IsEqualTo(room.Object().DBRef.Number);

		var oneHop = await _db.GetLocationAsync(coinRef, 1);
		await Assert.That(oneHop.WithoutNone().Object().DBRef.Number).IsEqualTo(boxRef.Number);
	}

	[Test]
	public async Task ParentsAsyncWalksTheWholeChain()
	{
		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known.AsPlayer;
		var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Known.AsContainer;

		var grandparentRef = await _db.CreateThingAsync("Grandparent", room, god, room);
		var parentRef = await _db.CreateThingAsync("Parent", room, god, room);
		var childRef = await _db.CreateThingAsync("Child", room, god, room);

		var grandparent = (await _db.GetObjectNodeAsync(grandparentRef)).Known;
		var parent = (await _db.GetObjectNodeAsync(parentRef)).Known;
		var child = (await _db.GetObjectNodeAsync(childRef)).Known;

		await _db.SetObjectParent(parent, grandparent);
		await _db.SetObjectParent(child, parent);

		var chain = (await _db.GetParentsAsync(childRef.Number.ToString()).ToListAsync())
			.Select(p => p.DBRef.Number).ToList();
		await Assert.That(chain).IsEquivalentTo([parentRef.Number, grandparentRef.Number]);
	}

	[Test]
	public async Task MoveObjectChangesContentsOfSourceAndDestination()
	{
		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known.AsPlayer;
		var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Known.AsContainer;
		var destinationRef = await _db.CreateRoomAsync("Elsewhere", god);
		var destination = (await _db.GetObjectNodeAsync(destinationRef)).Known.AsContainer;

		var thingRef = await _db.CreateThingAsync("Traveler", room, god, room);
		var thing = (await _db.GetObjectNodeAsync(thingRef)).Known.AsContent;

		await _db.MoveObjectAsync(thing, destination);

		var sourceContents = (await _db.GetContentsAsync(room).ToListAsync())
			.Select(c => c.Object().DBRef.Number).ToList();
		await Assert.That(sourceContents).DoesNotContain(thingRef.Number);

		var destinationContents = (await _db.GetContentsAsync(destination).ToListAsync())
			.Select(c => c.Object().DBRef.Number).ToList();
		await Assert.That(destinationContents).Contains(thingRef.Number);
	}
}
