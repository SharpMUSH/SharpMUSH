using Mediator;
using NSubstitute;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Services;

namespace SharpMUSH.Tests.Applications;

public class WorldMapReaderTests
{
	private readonly TestObjectFactory _objects = new();
	private readonly IMediator _mediator = Substitute.For<IMediator>();
	private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
	private readonly IAttributeService _attributes = Substitute.For<IAttributeService>();
	private readonly IRealityPolicy _reality = Substitute.For<IRealityPolicy>();
	private readonly Dictionary<(int Number, string Name), string> _values = new();
	private readonly Dictionary<int, List<SharpObject>> _zones = new();
	private readonly AnySharpObject _builder;
	private readonly AnySharpObject _viewer;
	private readonly SharpRoom _start;

	public WorldMapReaderTests()
	{
		_start = _objects.CreateRoom(1, "Elsewhere");
		_builder = _objects.CreatePlayer(2, "Builder", _start);
		_viewer = _objects.CreatePlayer(3, "Viewer", _start);
		_mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
			ValueTask.FromResult<AnyOptionalSharpObject>(Find(call.Arg<GetObjectNodeQuery>().DBRef.Number) is { } found ? found : new None()));
		_mediator.CreateStream(Arg.Any<GetObjectsByZoneQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
			(_zones.GetValueOrDefault(call.Arg<GetObjectsByZoneQuery>().Zone is DBRef zone ? zone.Number : -1) ?? []).ToAsyncEnumerable());
		_mediator.CreateStream(Arg.Any<GetExitsQuery>(), Arg.Any<CancellationToken>()).Returns(AsyncEnumerable.Empty<SharpExit>());
		_reality.CanPerceiveAsync(Arg.Any<DBRef>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(true);
		_connections.GetAll().Returns(_ => _connected.ToAsyncEnumerable());
		_permissions.CanExamine(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(false);
		_attributes.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<string>(),
			IAttributeService.AttributeMode.Read, false).Returns(call =>
		{
			var key = (call.ArgAt<AnySharpObject>(1).Object().DBRef.Number, call.ArgAt<string>(2));
			return ValueTask.FromResult<OptionalSharpAttributeOrError>(_values.TryGetValue(key, out var value)
				? new[] { Attribute(key.Item2, value) }
				: new None());
		});
	}

	private readonly IConnectionService _connections = Substitute.For<IConnectionService>();
	private readonly List<IConnectionService.ConnectionData> _connected = [];

	private WorldMapReader Reader => new(_mediator, _permissions, _reality, _attributes, _connections);

	private AnySharpObject? Find(int number) =>
		_objects.GetAllRooms().Select(room => new AnySharpObject(room)).Concat(_objects.GetAllObjects())
			.FirstOrDefault(o => o.Object().DBRef.Number == number);

	private static SharpAttribute Attribute(string name, string value) =>
		new("a", "a", name, [], null, name, null!, null!, null!) { Value = MarkupText.Plain(value) };

	private AnySharpObject Map(int number, string name, string kind)
	{
		var map = _objects.CreateThing(number, name, _start, _builder);
		_values[(number, WorldMapReader.KindAttribute)] = kind;
		return map;
	}

	private void Zone(AnySharpObject target, AnySharpObject zone)
	{
		target.Object().Zone = new(async _ =>
		{
			await ValueTask.CompletedTask;
			return zone;
		});
		_zones.TryAdd(zone.Object().DBRef.Number, []);
		_zones[zone.Object().DBRef.Number].Add(target.Object());
	}

	private SharpRoom Room(int number, string name, AnySharpObject map)
	{
		var room = _objects.CreateRoom(number, name);
		Zone(new AnySharpObject(room), map);
		return room;
	}

	private void Indexed(params int[] numbers) =>
		_mediator.Send(Arg.Any<GetIndexedAttributeQuery>(), Arg.Any<CancellationToken>()).Returns(
			ValueTask.FromResult<IndexedAttributeResult>(new IndexedAttributePage(
				[.. numbers.Select(number => new IndexedAttributeHit(number, 0, -1, IndexedAttributeFacts.None))], null)));

	private static void Dark(SharpRoom room) =>
		room.Object.Flags = new(() => new[]
		{
			new SharpObjectFlag
			{
				Name = "DARK", Symbol = "D", SetPermissions = [], UnsetPermissions = [], System = true, TypeRestrictions = []
			}
		}.ToAsyncEnumerable());

	[Test]
	public async Task ListsOnlyObjectsWhoseKindIsAKind()
	{
		Map(50, "Harbour", MapKinds.Coordinates);
		Map(51, "Not a map", "spiral");
		Map(52, "Caves", "Compass");
		Indexed(50, 51, 52);

		var maps = await Reader.ListAsync(_viewer, default);

		await Assert.That(maps.Select(map => (map.Id, map.Kind))).IsEquivalentTo([("#52", MapKinds.Compass), ("#50", MapKinds.Coordinates)]);
		await Assert.That(maps[0].Name).IsEqualTo("Caves");
	}

	[Test]
	public async Task ParentIsTheMapsZoneWhenThatIsAMap()
	{
		var city = Map(50, "City", MapKinds.Graph);
		var harbour = Map(51, "Harbour", MapKinds.Coordinates);
		var caves = Map(52, "Caves", MapKinds.Compass);
		Zone(harbour, city);
		Zone(caves, _viewer);
		Zone(city, city);
		Indexed(50, 51, 52);

		var maps = (await Reader.ListAsync(_viewer, default)).ToDictionary(map => map.Id);

		await Assert.That(maps["#51"].Parent).IsEqualTo("#50");
		await Assert.That(maps["#52"].Parent).IsNull().Because("#3 is a player, not a map");
		await Assert.That(maps["#50"].Parent).IsNull().Because("a map zoned to itself is inside nothing");
	}

	[Test]
	public async Task PositionsAreReadAsTheMapsKindSays()
	{
		var grid = Map(50, "Harbour", MapKinds.Coordinates);
		Room(10, "Quay", grid);
		_values[(10, "MAP`COORDS`X")] = "3";
		_values[(10, "MAP`COORDS`Y")] = "-1";
		_values[(10, "MAP`POS`X")] = "900";
		_values[(10, "MAP`SIZE`W")] = "2";

		var map = (await Reader.ReadAsync(_viewer, 50, default)).Expect<MapDto>();

		await Assert.That(map.Rooms.Single().Position).IsEquivalentTo([3d, -1d, 0d]);
		await Assert.That(map.Rooms.Single().Size).IsEquivalentTo([2d, 1d]);

		var picture = Map(51, "City", MapKinds.Graph);
		Room(11, "Square", picture);
		_values[(11, "MAP`POS`X")] = "120.5";
		_values[(11, "MAP`POS`Y")] = "80";
		_values[(11, "MAP`COORDS`X")] = "3";

		var city = (await Reader.ReadAsync(_viewer, 51, default)).Expect<MapDto>();

		await Assert.That(city.Rooms.Single().Position).IsEquivalentTo([120.5, 80d]);
		await Assert.That(city.Rooms.Single().Size).IsNull();
	}

	[Test]
	public async Task ADarkRoomIsLeftOutForAViewerWhoMayNotExamineIt()
	{
		var grid = Map(50, "Harbour", MapKinds.Coordinates);
		Room(10, "Quay", grid);
		Dark(Room(11, "Smugglers' cellar", grid));

		var map = (await Reader.ReadAsync(_viewer, 50, default)).Expect<MapDto>();
		await Assert.That(map.Rooms.Select(room => room.Id)).IsEquivalentTo(["#10"]);

		_permissions.CanExamine(_builder, Arg.Any<AnySharpObject>()).Returns(true);
		var built = (await Reader.ReadAsync(_builder, 50, default)).Expect<MapDto>();
		await Assert.That(built.Rooms.Select(room => room.Id)).IsEquivalentTo(["#10", "#11"]);
	}

	[Test]
	public async Task AnExitToAnotherMapNamesThatMap()
	{
		var harbour = Map(50, "Harbour", MapKinds.Coordinates);
		var city = Map(51, "City", MapKinds.Graph);
		var quay = Room(10, "Quay", harbour);
		var gate = Room(11, "Gate", harbour);
		var square = Room(20, "Square", city);
		var north = _objects.CreateExit(30, "North", ["n"], quay, gate).Expect<SharpExit>();
		var road = _objects.CreateExit(31, "Road", [], quay, square).Expect<SharpExit>();
		_mediator.CreateStream(Arg.Any<GetExitsQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
			call.Arg<GetExitsQuery>().DBRef is DBRef { Number: 10 } ? new[] { north, road }.ToAsyncEnumerable() : AsyncEnumerable.Empty<SharpExit>());

		var map = (await Reader.ReadAsync(_viewer, 50, default)).Expect<MapDto>();
		var exits = map.Exits.ToDictionary(exit => exit.Id);

		await Assert.That(exits["#30"].Destination).IsEqualTo("#11");
		await Assert.That(exits["#30"].Aliases).IsEquivalentTo(["n"]);
		await Assert.That(exits["#30"].DestinationMap).IsNull();
		await Assert.That(exits["#31"].Destination).IsEqualTo("#20");
		await Assert.That(exits["#31"].DestinationMap?.Name).IsEqualTo("City");
	}

	[Test]
	public async Task AnObjectWithoutAKindIsNoMap()
	{
		_objects.CreateThing(50, "Plain thing", _start, _builder);

		await Assert.That(await Reader.ReadAsync(_viewer, 50, default) is NotFound).IsTrue();
	}

	[Test]
	public async Task TheViewersRoomIsHereOnlyOnItsOwnMap()
	{
		var harbour = Map(50, "Harbour", MapKinds.Coordinates);
		var quay = Room(10, "Quay", harbour);
		var viewer = _objects.CreatePlayer(4, "Sailor", quay);

		await Assert.That((await Reader.ReadAsync(viewer, 50, default)).Expect<MapDto>().Here).IsEqualTo("#10");
		await Assert.That((await Reader.ReadAsync(_viewer, 50, default)).Expect<MapDto>().Here).IsNull();
	}

	[Test]
	public async Task ARegionIsAMapZonedToThisOneAndHereFollowsTheZonesUp()
	{
		var earth = Map(50, "Earth", MapKinds.Graph);
		var states = Map(51, "United States", MapKinds.Coordinates);
		var city = Map(52, "New York", MapKinds.Coordinates);
		var hidden = Map(53, "Secret base", MapKinds.Graph);
		var rock = _objects.CreateThing(54, "Rock", _start, _builder);
		Zone(states, earth);
		Zone(hidden, earth);
		Zone(rock, earth);
		Zone(city, states);
		var street = Room(10, "Flatbush Avenue", city);
		_values[(51, "MAP`POS`X")] = "300";
		_values[(51, "MAP`POS`Y")] = "120";
		_values[(51, "MAP`SIZE`W")] = "200";
		_reality.CanPerceiveAsync(Arg.Any<DBRef>(), Arg.Is<DBRef>(target => target.Number == 53), Arg.Any<CancellationToken>()).Returns(false);
		var walker = _objects.CreatePlayer(4, "Walker", street);

		var map = (await Reader.ReadAsync(walker, 50, default)).Expect<MapDto>();

		await Assert.That(map.Regions.Select(region => (region.Id, region.Name))).IsEquivalentTo([("#51", "United States")])
			.Because("the rock is no map, and the viewer cannot perceive the secret base");
		await Assert.That(map.Regions.Single().Position).IsEquivalentTo([300d, 120d]);
		await Assert.That(map.Regions.Single().Size).IsEquivalentTo([200d, 1d]);
		await Assert.That(map.HereRegion).IsEqualTo("#51").Because("New York, the street's zone, is zoned to the United States");
		await Assert.That(map.Here).IsNull();
		await Assert.That((await Reader.ReadAsync(_viewer, 50, default)).Expect<MapDto>().HereRegion).IsNull();
	}

	[Test]
	public async Task AMapNamesItsLevelsAndTheOneItOpensOn()
	{
		var harbour = Map(50, "Harbour", MapKinds.Coordinates);
		_values[(50, WorldMapReader.LevelAttribute)] = "-1";
		_attributes.GetAttributePatternAsync(harbour, harbour, "MAP`LEVEL`*", false, IAttributeService.AttributePatternMode.Wildcard)
			.Returns(new SharpAttributesOrError(new[]
			{
				Attribute("MAP`LEVEL`-1", "Sewers"), Attribute("MAP`LEVEL`0", "Streets"), Attribute("MAP`LEVEL`1", " "),
				Attribute("MAP`LEVEL`ROOF", "Rooftops"),
			}));

		var map = (await Reader.ReadAsync(_viewer, 50, default)).Expect<MapDto>();

		await Assert.That(map.Level).IsEqualTo(-1);
		await Assert.That(map.LevelNames).IsEquivalentTo(new Dictionary<int, string> { [-1] = "Sewers", [0] = "Streets" });
	}

	[Test]
	public async Task ARoomsEnvironmentIsDrawnByTheNearestMapThatNamesIt()
	{
		var town = Map(50, "Town", MapKinds.Compass);
		var harbour = Map(51, "Harbour", MapKinds.Coordinates);
		Zone(harbour, town);
		Room(10, "Quay", harbour);
		_values[(10, WorldMapReader.EnvironmentAttribute)] = "Water";
		Room(11, "Lane", harbour);
		_attributes.GetAttributePatternAsync(harbour, harbour, "MAP`LEGEND`*`*", false, IAttributeService.AttributePatternMode.Wildcard)
			.Returns(new SharpAttributesOrError(new[]
			{
				Attribute("MAP`LEGEND`WATER`TONE", "Blue"), Attribute("MAP`LEGEND`WATER`ICON", "≈"),
				Attribute("MAP`LEGEND`DOCK`LABEL", " "),
			}));
		_attributes.GetAttributePatternAsync(town, town, "MAP`LEGEND`*`*", false, IAttributeService.AttributePatternMode.Wildcard)
			.Returns(new SharpAttributesOrError(new[]
			{
				Attribute("MAP`LEGEND`WATER`LABEL", "Sea"), Attribute("MAP`LEGEND`STREET`LABEL", "Street"),
				Attribute("MAP`LEGEND`STREET`TONE", "muted"),
			}));

		var map = (await Reader.ReadAsync(_viewer, 51, default)).Expect<MapDto>();

		await Assert.That(map.Rooms.Select(room => (room.Id, room.Environment))).IsEquivalentTo([("#10", (string?)"water"), ("#11", null)]);
		await Assert.That(map.Legend).IsEquivalentTo([
			new MapLegendDto("street", "Street", "muted", null),
			new MapLegendDto("water", "water", "blue", "≈"),
		]).Because("Harbour names water itself, so Town's label for it is not used; a blank value names nothing");
	}

	[Test]
	public async Task ARoomListsTheConnectedPlayersTheViewerMayLocateThere()
	{
		var harbour = Map(50, "Harbour", MapKinds.Coordinates);
		var quay = Room(10, "Quay", harbour);
		var sailor = _objects.CreatePlayer(4, "Sailor", quay);
		var spy = _objects.CreatePlayer(5, "Spy", quay);
		_objects.CreatePlayer(6, "Ashore", _start);
		_objects.CreatePlayer(7, "Asleep", quay);
		foreach (var (number, handle) in new[] { (4, 1L), (4, 2L), (5, 3L), (6, 4L) })
			_connected.Add(new(handle, new DBRef(number), IConnectionService.ConnectionState.LoggedIn, _ => ValueTask.CompletedTask,
				_ => ValueTask.CompletedTask, () => System.Text.Encoding.UTF8, new()));
		_permissions.CanLocate(_viewer, Arg.Any<AnySharpObject>()).Returns(call => call.ArgAt<AnySharpObject>(1).Object().Name != "Spy");
		_permissions.CanSee(_viewer, Arg.Any<AnySharpObject>()).Returns(true);

		var map = (await Reader.ReadAsync(_viewer, 50, default)).Expect<MapDto>();

		await Assert.That(map.Rooms.Single().People).IsEquivalentTo(["Sailor"])
			.Because("the spy is unfindable to the viewer, Asleep is not connected, and Ashore is on no room of this map");
	}

	[Test]
	public async Task AMapSendsItsBackdropAsStored()
	{
		Map(50, "Isle", MapKinds.Graph);
		_values[(50, WorldMapReader.BackdropAttribute)] = "/api/wiki-assets/7/isle.webp";
		Map(51, "Plain", MapKinds.Graph);

		var isle = (await Reader.ReadAsync(_viewer, 50, default)).Expect<MapDto>();
		var plain = (await Reader.ReadAsync(_viewer, 51, default)).Expect<MapDto>();

		await Assert.That(isle.Backdrop).IsEqualTo("/api/wiki-assets/7/isle.webp");
		await Assert.That(plain.Backdrop).IsNull();
	}

	private readonly Dictionary<int, List<SharpExit>> _exits = new();

	/// <summary>An exit from <paramref name="from"/> to <paramref name="to"/>, owned by the builder, listed in its room.</summary>
	private void Exit(int number, string name, string[] aliases, SharpRoom from, SharpRoom to)
	{
		if (_objects.CreateExit(number, name, aliases, from, to) is not SharpExit exit || _builder is not SharpPlayer builder)
			throw new InvalidOperationException("The factory made no exit.");
		exit.Object.Owner = new(async _ =>
		{
			await ValueTask.CompletedTask;
			return builder;
		});
		_exits.TryAdd(from.Object.DBRef.Number, []);
		_exits[from.Object.DBRef.Number].Add(exit);
		_mediator.CreateStream(Arg.Any<GetExitsQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
			(call.Arg<GetExitsQuery>().DBRef is DBRef room ? _exits.GetValueOrDefault(room.Number) ?? [] : []).ToAsyncEnumerable());
	}

	[Test]
	public async Task ARouteCrossesMapsAndWalksAsOneLine()
	{
		var town = Map(50, "Town", MapKinds.Compass);
		var coast = Map(60, "Coast", MapKinds.Graph);
		var gate = Room(10, "Gate", town);
		var square = Room(11, "Square", town);
		var beach = Room(20, "Beach", coast);
		Exit(100, "Gate", ["g"], _start, gate);
		Exit(101, "North", ["n"], gate, square);
		Exit(102, "Coast road", ["road"], square, beach);
		// A longer way round is not taken.
		Exit(103, "Lane", [], gate, _objects.CreateRoom(12, "Lane"));
		Exit(104, "On", [], _objects.CreateRoom(12, "Lane"), square);

		var route = (await Reader.RouteAsync(_viewer, 20, default)).Expect<MapRouteDto>();

		await Assert.That(route.Steps.Select(step => (step.Exit, step.Destination, step.Map?.Id ?? "none")))
			.IsEquivalentTo([("#100", "#10", "#50"), ("#101", "#11", "#50"), ("#102", "#20", "#60")]);
		await Assert.That(route.Command).IsEqualTo("walk Gate North road");
	}

	[Test]
	public async Task AnExitWithNoNameWithoutASpaceIsWalkedByItsNameInQuotes()
	{
		var town = Map(50, "Town", MapKinds.Compass);
		var gate = Room(10, "Gate", town);
		Exit(100, "Old gate", ["The arch"], _start, gate);

		var route = (await Reader.RouteAsync(_viewer, 10, default)).Expect<MapRouteDto>();

		await Assert.That(route.Steps).Count().IsEqualTo(1);
		await Assert.That(route.Command).IsEqualTo("walk \"Old gate\"");
	}

	[Test]
	[Arguments("Port\"side", new[] { "port" }, "port")]
	[Arguments("Port\"side", new string[0], null)]
	[Arguments("Old \"gate\"", new string[0], null)]
	public async Task AWordNeverCarriesAQuoteWalkWouldSplitOn(string name, string[] aliases, string? word)
		=> await Assert.That(WorldMapReader.Word(name, aliases)).IsEqualTo(word);

	[Test]
	public async Task NoRouteToARoomNoExitReaches()
	{
		var town = Map(50, "Town", MapKinds.Compass);
		Room(10, "Gate", town);
		Room(11, "Island", town);
		Exit(100, "Gate", [], _start, _objects.CreateRoom(10, "Gate"));

		await Assert.That(await Reader.RouteAsync(_viewer, 11, default) is NotFound).IsTrue();
		await Assert.That((await Reader.RouteAsync(_viewer, 1, default)).Expect<MapRouteDto>().Steps).IsEmpty();
	}

	[Test]
	public async Task ABuilderWhoControlsTheMapMayEditItAndAddTheRoomsItsExitsReach()
	{
		var harbour = Map(50, "Harbour", MapKinds.Coordinates);
		var caves = Map(51, "Caves", MapKinds.Compass);
		var quay = Room(10, "Quay", harbour);
		Room(11, "Theirs", harbour);
		Exit(100, "Shed", [], quay, _objects.CreateRoom(20, "Shed"));
		Exit(101, "Cellar", [], quay, _objects.CreateRoom(21, "Cellar"));
		Exit(102, "Grotto", [], quay, Room(22, "Grotto", caves));
		Exit(103, "Shed door", [], quay, _objects.CreateRoom(20, "Shed"));
		_permissions.Controls(_builder, Arg.Any<AnySharpObject>()).Returns(call =>
			call.ArgAt<AnySharpObject>(1).Object().DBRef.Number is 50 or 10 or 20 or 22);

		var map = (await Reader.ReadAsync(_builder, 50, default)).Expect<MapDto>();

		await Assert.That(map.CanEdit).IsTrue();
		await Assert.That(map.Rooms.Select(room => (room.Name, room.Editable))).IsEquivalentTo([("Quay", true), ("Theirs", false)]);
		await Assert.That(map.Nearby.Select(room => room.Id)).IsEquivalentTo(["#20"])
			.Because("the cellar is not the builder's, and the grotto is on the caves map already");
	}

	[Test]
	public async Task AViewerWhoDoesNotControlTheMapIsOfferedNothing()
	{
		var harbour = Map(50, "Harbour", MapKinds.Coordinates);
		var quay = Room(10, "Quay", harbour);
		Exit(100, "Shed", [], quay, _objects.CreateRoom(20, "Shed"));
		_permissions.Controls(_viewer, Arg.Any<AnySharpObject>()).Returns(call => call.ArgAt<AnySharpObject>(1).Object().DBRef.Number is 10 or 20);

		var map = (await Reader.ReadAsync(_viewer, 50, default)).Expect<MapDto>();

		await Assert.That(map.CanEdit).IsFalse();
		await Assert.That(map.Rooms.Single().Editable).IsFalse().Because("a room is changed through its map");
		await Assert.That(map.Nearby).IsEmpty();
	}

	[Test]
	public async Task ARoomsPlaceIsReadAsTheRoomNotTheMapsOwner()
	{
		var harbour = Map(50, "Harbour", MapKinds.Coordinates);
		Room(10, "Quay", harbour);
		_values[(10, "MAP`COORDS`X")] = "2";
		// The map's owner may not read the attributes of a room a wizard dug into the map.
		_attributes.GetAttributeAsync(Arg.Is<AnySharpObject>(reader => reader.Object().DBRef.Number != 10),
				Arg.Is<AnySharpObject>(target => target.Object().DBRef.Number == 10), Arg.Any<string>(), IAttributeService.AttributeMode.Read, false)
			.Returns(ValueTask.FromResult<OptionalSharpAttributeOrError>(new None()));

		var map = (await Reader.ReadAsync(_viewer, 50, default)).Expect<MapDto>();

		await Assert.That(map.Rooms.Single().Position).IsEquivalentTo([2.0, 0.0, 0.0]);
	}
}
