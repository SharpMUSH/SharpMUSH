using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// <see cref="INavigationStore"/> ported onto the LMDB edge tables: locations, contents, exits,
/// entrances, homed-at, parent chains and reachability.
/// </summary>
public partial class NavigationTests
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
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		await FixtureDirectoryCleanup.DeleteAsync(_path);
	}

	private async Task<SharpPlayer> God() => (await _db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();

	private async Task<AnySharpContainer> MasterRoom() => (await Node(new DBRef(2))).AsOptionalContainer.Expect<AnySharpContainer>();

	private async Task<AnySharpObject> Node(DBRef dbref) => (await _db.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>();

	[Test]
	public async Task ReachabilityFollowsParentsAndZonesWithoutLooping()
	{
		var god = await God();
		var room = await MasterRoom();
		var aRef = await _db.CreateThingAsync("A", room, god, room);
		var bRef = await _db.CreateThingAsync("B", room, god, room);
		var cRef = await _db.CreateThingAsync("C", room, god, room);

		var a = await Node(aRef);
		var b = await Node(bRef);
		var c = await Node(cRef);

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
		var god = await God();
		var room = await MasterRoom();

		var aRef = await _db.CreateThingAsync("A", room, god, room);
		var xRef = await _db.CreateThingAsync("X", room, god, room);
		var yRef = await _db.CreateThingAsync("Y", room, god, room);
		var zRef = await _db.CreateThingAsync("Z", room, god, room);

		var a = await Node(aRef);
		var x = await Node(xRef);
		var y = await Node(yRef);
		var z = await Node(zRef);

		await _db.SetObjectParent(a, x);
		await _db.SetObjectZone(a, y);
		await _db.SetObjectParent(y, z);

		await Assert.That(await _db.IsReachableViaParentOrZoneAsync(a, z, 100)).IsTrue();
	}

	[Test]
	public async Task ReachabilityFindsTargetThroughParentWhenZoneIsADeadEnd()
	{
		// Mirrored: A.zone = X (dead end), A.parent = Y, Y.zone = Z (target).
		var god = await God();
		var room = await MasterRoom();

		var aRef = await _db.CreateThingAsync("A", room, god, room);
		var xRef = await _db.CreateThingAsync("X", room, god, room);
		var yRef = await _db.CreateThingAsync("Y", room, god, room);
		var zRef = await _db.CreateThingAsync("Z", room, god, room);

		var a = await Node(aRef);
		var x = await Node(xRef);
		var y = await Node(yRef);
		var z = await Node(zRef);

		await _db.SetObjectZone(a, x);
		await _db.SetObjectParent(a, y);
		await _db.SetObjectZone(y, z);

		await Assert.That(await _db.IsReachableViaParentOrZoneAsync(a, z, 100)).IsTrue();
	}

	[Test]
	public async Task ContentsAndExitsListWhatWasCreatedInARoom()
	{
		var god = await God();
		var room = await MasterRoom();

		var thingRef = await _db.CreateThingAsync("Gadget", room, god, room);
		var exitRef = await _db.CreateExitAsync("Out", ["O"], room, god);

		var contentKeys = (await _db.GetContentRefsAsync(room.Object().DBRef).ToListAsync())
			.Select(c => c.Number).ToList();
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
		var god = await God();
		var room = await MasterRoom();
		var destinationRef = await _db.CreateRoomAsync("Destination", god);
		var destination = (await Node(destinationRef)).AsOptionalContainer.Expect<AnySharpContainer>();

		var exitRef = await _db.CreateExitAsync("Out", ["O"], room, god);
		var exit = (await _db.GetObjectNodeAsync(exitRef)).Expect<SharpExit>();
		await _db.LinkExitAsync(exit, destination);

		var entranceKeys = (await _db.GetEntrancesAsync(destinationRef).ToListAsync())
			.Select(e => e.Object.DBRef.Number).ToList();
		await Assert.That(entranceKeys).Contains(exitRef.Number);
	}

	[Test]
	public async Task HomedAtExcludesRoomsButIncludesThingsAndExits()
	{
		var god = await God();
		var room = await MasterRoom();

		var homeRef = await _db.CreateRoomAsync("Home Base", god);
		var home = (await Node(homeRef)).AsOptionalContainer.Expect<AnySharpContainer>();

		var thingRef = await _db.CreateThingAsync("Wanderer", room, god, home);

		// An exit's destination reuses the same home edge (LinkExitAsync), same as a
		// room's drop-to — it must show up here, unlike the room case below.
		var exitRef = await _db.CreateExitAsync("Out", ["O"], room, god);
		var exit = (await _db.GetObjectNodeAsync(exitRef)).Expect<SharpExit>();
		await _db.LinkExitAsync(exit, home);

		// A room's own drop-to also reuses the home edge — must not show up as "homed at" the target.
		var dropRoomRef = await _db.CreateRoomAsync("Drops Here", god);
		var dropRoom = (await _db.GetObjectNodeAsync(dropRoomRef)).Expect<SharpRoom>();
		await _db.LinkRoomAsync(dropRoom, home.WithNoneOption());

		var homedKeys = (await _db.GetHomedAtAsync(homeRef).ToListAsync())
			.Select(c => c.Object().DBRef.Number).ToList();
		await Assert.That(homedKeys).Contains(thingRef.Number);
		await Assert.That(homedKeys).Contains(exitRef.Number);
		await Assert.That(homedKeys).DoesNotContain(dropRoomRef.Number);
	}

	[Test]
	public async Task RelationRefsNameTheFullObjectIdOrNothing()
	{
		var god = await God();
		var room = await MasterRoom();
		var parentRef = await _db.CreateThingAsync("Parent", room, god, room);
		var childRef = await _db.CreateThingAsync("Child", room, god, room);
		var child = await Node(childRef);
		var parent = await Node(parentRef);
		var notFound = new Found<DBRef>(new NotFound());

		await Assert.That(await _db.GetRelationRefAsync(ObjectRelationKind.Parent, childRef)).IsEqualTo(notFound);

		await _db.SetObjectParent(child, parent);
		await _db.SetObjectZone(child, parent);

		await Assert.That((await _db.GetRelationRefAsync(ObjectRelationKind.Parent, childRef)).Expect<DBRef>()).IsEqualTo(parent.Object().DBRef);
		await Assert.That((await _db.GetRelationRefAsync(ObjectRelationKind.Zone, childRef)).Expect<DBRef>()).IsEqualTo(parent.Object().DBRef);
		await Assert.That((await _db.GetRelationRefAsync(ObjectRelationKind.Owner, childRef)).Expect<DBRef>()).IsEqualTo(god.Object.DBRef);
		await Assert.That((await _db.GetRelationRefAsync(ObjectRelationKind.Home, childRef)).Expect<DBRef>()).IsEqualTo(room.Object().DBRef);

		// A full object id whose creation time no longer matches names a recycled number: nothing.
		var stale = new DBRef(childRef.Number, child.Object().CreationTime - 1);
		await Assert.That(await _db.GetRelationRefAsync(ObjectRelationKind.Parent, stale)).IsEqualTo(notFound);
		await Assert.That(await _db.GetLocationRefAsync(stale)).IsEqualTo(notFound);
	}

	[Test]
	public async Task ContentAndPlayerRefsLeaveOutWhatTheyDoNotName()
	{
		var god = await God();
		var room = await MasterRoom();
		var thingRef = await _db.CreateThingAsync("NotAPlayer", room, god, room);
		// A room whose location edge names the master room (an imported world can carry one) is not content.
		var dropRoomRef = await _db.CreateRoomAsync("LocatedInMaster", god);
		await _db.Store.WriteAsync(tx => LightningDatabase.SetSingleEdge(tx, Tables.Location, dropRoomRef.Number, room.Object().DBRef.Number));

		var contents = await _db.GetContentRefsAsync(room.Object().DBRef).ToListAsync();
		await Assert.That(contents).Contains((await Node(thingRef)).Object().DBRef);
		await Assert.That(contents.Select(c => c.Number)).DoesNotContain(dropRoomRef.Number);

		await Assert.That(await _db.GetPlayerRefsByNameOrAliasAsync(god.Object.Name).ToListAsync()).IsEquivalentTo(new[] { god.Object.DBRef });
		await Assert.That(await _db.GetPlayerRefsByNameOrAliasAsync("NotAPlayer").ToListAsync()).IsEmpty();
	}

	[Test]
	public async Task LocationDepthNegativeOneWalksToTheTop()
	{
		var god = await God();
		var room = await MasterRoom();

		var boxRef = await _db.CreateThingAsync("Box", room, god, room);
		var boxNode = await Node(boxRef);
		var coinRef = await _db.CreateThingAsync("Coin", room, god, room);
		var coin = (await Node(coinRef)).AsOptionalContent.Expect<AnySharpContent>();

		// Coin sits inside the box, which sits in the room: two hops to the room.
		await _db.SetContentLocation(coin, boxNode.AsOptionalContainer.Expect<AnySharpContainer>());
		await _db.SetContentLocation(boxNode.AsOptionalContent.Expect<AnySharpContent>(), room);

		var top = (await _db.GetLocationRefAsync(coinRef, -1)).Expect<DBRef>();
		await Assert.That(top).IsEqualTo(room.Object().DBRef);

		var oneHop = (await _db.GetLocationRefAsync(coinRef, 1)).Expect<DBRef>();
		await Assert.That(oneHop.Number).IsEqualTo(boxRef.Number);

		await Assert.That(await _db.GetLocationRefAsync(coinRef, 0)).IsEqualTo(new Found<DBRef>(new NotFound()))
			.Because("depth 0 takes no hop, so it names nothing rather than the object itself");
	}

	[Test]
	public async Task ParentsAsyncWalksTheWholeChain()
	{
		var god = await God();
		var room = await MasterRoom();

		var grandparentRef = await _db.CreateThingAsync("Grandparent", room, god, room);
		var parentRef = await _db.CreateThingAsync("Parent", room, god, room);
		var childRef = await _db.CreateThingAsync("Child", room, god, room);

		var grandparent = await Node(grandparentRef);
		var parent = await Node(parentRef);
		var child = await Node(childRef);

		await _db.SetObjectParent(parent, grandparent);
		await _db.SetObjectParent(child, parent);

		var chain = (await _db.GetParentsAsync(childRef.Number.ToString()).ToListAsync())
			.Select(p => p.DBRef.Number).ToList();
		await Assert.That(chain).IsEquivalentTo([parentRef.Number, grandparentRef.Number]);
	}

	[Test]
	public async Task MoveObjectChangesContentsOfSourceAndDestination()
	{
		var god = await God();
		var room = await MasterRoom();
		var destinationRef = await _db.CreateRoomAsync("Elsewhere", god);
		var destination = (await Node(destinationRef)).AsOptionalContainer.Expect<AnySharpContainer>();

		var thingRef = await _db.CreateThingAsync("Traveler", room, god, room);
		var thing = (await Node(thingRef)).AsOptionalContent.Expect<AnySharpContent>();

		await _db.MoveObjectAsync(thing, destination);

		var sourceContents = (await _db.GetContentRefsAsync(room.Object().DBRef).ToListAsync())
			.Select(c => c.Number).ToList();
		await Assert.That(sourceContents).DoesNotContain(thingRef.Number);

		var destinationContents = (await _db.GetContentRefsAsync(destination.Object().DBRef).ToListAsync())
			.Select(c => c.Number).ToList();
		await Assert.That(destinationContents).Contains(thingRef.Number);
	}
}
