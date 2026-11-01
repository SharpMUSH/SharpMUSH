using SharpMUSH.Client.Components.Map;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.BUnit.Components;

public class MapPlanTests
{
	private static MapDto Grid(params MapRoomDto[] rooms) =>
		new(new("#50", "Harbour", MapKinds.Coordinates, null), null, rooms, [], null, false) { CanEdit = true };

	private static MapRoomDto Room(string id, string name, double[]? position) => new(id, name, position, null) { Editable = true };

	private static IEnumerable<string> Commands(MapChange change) => change.Steps.Select(step => step.Command);

	[Test]
	public async Task PlacingARoomTwiceIsOneChange()
	{
		var plan = new MapPlan(Grid(Room("#10", "Quay", [0, 0, 0])));

		plan.Place("#10", 1, 1);
		plan.Place("#10", 2, 3);

		await Assert.That(plan.Changes).Count().IsEqualTo(1);
		await Assert.That(Commands(plan.Changes[0])).IsEquivalentTo(["&MAP`COORDS`X #10=2", "&MAP`COORDS`Y #10=3"]);
		await Assert.That(plan.Preview().Rooms.Single().Position).IsEquivalentTo([2.0, 3.0, 0.0]);
	}

	[Test]
	public async Task ARoomOnAPictureIsPlacedByItsPoint()
	{
		var map = Grid(Room("#10", "Ford", [10, 10])) with { Map = new("#53", "Isle", MapKinds.Graph, null) };
		var plan = new MapPlan(map);

		plan.Place("#10", 470.4, 390);

		await Assert.That(Commands(plan.Changes[0])).IsEquivalentTo(["&MAP`POS`X #10=470.4", "&MAP`POS`Y #10=390"]);
	}

	[Test]
	public async Task OneByOneClearsTheSize()
	{
		var plan = new MapPlan(Grid(new MapRoomDto("#10", "Hall", [0, 0, 0], [2, 2]) { Editable = true }));

		plan.Size("#10", 1, 1);

		await Assert.That(Commands(plan.Changes[0])).IsEquivalentTo(["&MAP`SIZE`W #10=", "&MAP`SIZE`H #10="]);
		await Assert.That(plan.Preview().Rooms.Single().Size).IsNull();
	}

	[Test]
	public async Task ADigMakesTheRoomZonesPlacesAndJoinsIt()
	{
		var plan = new MapPlan(Grid(Room("#10", "Quay", [0, 0, 0])));

		var key = plan.Dig("Boathouse", 1, 0, 0, "#10", "East;e", "West;w");

		var dig = plan.Changes.Single();
		await Assert.That(Commands(dig)).IsEquivalentTo(
		[
			"@dig Boathouse",
			$"@chzone {{{key}}}=#50",
			$"&MAP`COORDS`X {{{key}}}=1",
			$"&MAP`COORDS`Y {{{key}}}=0",
			$"@open East;e={{{key}}},West;w,#10",
		]);
		await Assert.That(plan.Show(dig.Steps[^1].Command)).IsEqualTo("@open East;e=<Boathouse>,West;w,#10");
		await Assert.That(dig.Steps[^1].Check)
			.IsEqualTo($"and(strmatch(num(loc(%>)),num({{{key}}})),strmatch(num(loc(lexits({{{key}}}))),num(#10)))")
			.Because("@open names the exit it made even when linking it or opening the way back failed");
		await Assert.That(dig.Steps[^1].Expect).IsEqualTo("1");
		var preview = plan.Preview();
		await Assert.That(preview.Rooms.Select(room => room.Name)).IsEquivalentTo(["Quay", "Boathouse"]);
		await Assert.That(preview.Exits.Select(exit => (exit.Name, exit.Source, exit.Destination)))
			.IsEquivalentTo([("East", "#10", key), ("West", key, "#10")]);
	}

	[Test]
	public async Task ARoomDugFromADugRoomIsJoinedToItOnceItExists()
	{
		var plan = new MapPlan(Grid(Room("#10", "Quay", [0, 0, 0])));
		var shed = plan.Dig("Shed", 1, 0, 0, "#10", "East;e", null);
		var loft = plan.Dig("Loft", 1, 0, 1, shed, "Up;u", "Down;d");

		var open = plan.Changes[1].Steps[^1].Command;

		await Assert.That(MapPlan.Resolve(open, new Dictionary<string, string> { [shed] = "#90:1", [loft] = "#91:2" }))
			.IsEqualTo("@open Up;u=#91:2,Down;d,#90:1");
		await Assert.That(plan.Changes[1].Steps.Select(step => step.Command)).Contains($"&MAP`COORDS`Z {{{loft}}}=1");

		plan.Drop(plan.Changes[0].Key);

		await Assert.That(plan.Changes).IsEmpty().Because("the loft would have no room to be joined from");
	}

	[Test]
	public async Task TakingOffARoomTheMapAlreadyHasUnzonesIt()
	{
		var plan = new MapPlan(Grid(Room("#10", "Quay", [0, 0, 0]), Room("#11", "Shed", [1, 0, 0])));

		plan.Place("#11", 4, 4);
		plan.Remove("#11");

		var change = plan.Changes.Single();
		await Assert.That(change.Kind).IsEqualTo(MapChangeKind.Remove);
		await Assert.That(Commands(change)).IsEquivalentTo(["@chzone #11=none"]);
		await Assert.That(plan.Preview().Rooms.Select(room => room.Id)).IsEquivalentTo(["#10"]);
	}

	[Test]
	public async Task TakingOffARoomThePlanAddsOnlyDropsTheAdd()
	{
		var plan = new MapPlan(Grid(Room("#10", "Quay", [0, 0, 0])));

		plan.Add(Room("#31", "Boathouse", null));
		await Assert.That(Commands(plan.Changes.Single())).IsEquivalentTo(["@chzone #31=#50"]);

		plan.Remove("#31");

		await Assert.That(plan.Changes).IsEmpty();
		await Assert.That(plan.Preview().Rooms.Select(room => room.Id)).IsEquivalentTo(["#10"]);
	}

	[Test]
	public async Task AnExitWithNoWayBackIsOneWay()
	{
		var map = Grid(Room("#0", "Room Zero", [0, 0, 0]), Room("#87", "Room One", [1, 0, 0])) with
		{
			Exits = [new("#88", "Next", [], "#0", "#87", null), new("#89", "Back", [], "#87", "#0", null)],
		};

		await Assert.That(MapPlan.OneWay(map)).IsEmpty().Because("Back leads from Room One to Room Zero");
		await Assert.That(MapPlan.OneWay(map with { Exits = map.Exits.Take(1).ToArray() })).IsEquivalentTo([new MissingWayBack(map.Exits[0], null)]);
	}

	[Test]
	public async Task AnEastExitWantsAWestOneBack()
	{
		var east = new MapExitDto("#132", "East", ["e"], "#0", "#87", null);
		var map = Grid(Room("#0", "Room Zero", [0, 0, 0]), Room("#87", "Room One", [1, 0, 0])) with
		{
			Exits = [east, new("#89", "Back", [], "#87", "#0", null)],
		};

		await Assert.That(MapPlan.OneWay(map)).IsEquivalentTo([new MissingWayBack(east, "West")])
			.Because("a player who went east tries west to come back, not Back");
		await Assert.That(MapPlan.OneWay(map with { Exits = [east, new("#133", "w", [], "#87", "#0", null)] })).IsEmpty();
		await Assert.That(MapPlan.OneWay(map with { Exits = [east, new("#133", "Gate", ["up", "w"], "#87", "#0", null)] })
			.Select(missing => missing.Exit.Id)).DoesNotContain("#132")
			.Because("an alias of the way back is West, whatever its first direction name");
		await Assert.That(MapPlan.OneWay(map with { Exits = [east], Truncated = true })).IsEmpty()
			.Because("a truncated map may have left the way back out");
	}

	[Test]
	public async Task APlannedWayBackEndsTheOneWay()
	{
		var map = Grid(Room("#0", "Room Zero", [0, 0, 0]), Room("#87", "Room One", [1, 0, 0])) with
		{
			Exits = [new("#132", "East", ["e"], "#0", "#87", null)],
		};
		var plan = new MapPlan(map);

		plan.Join("#87", "#0", MapPlan.WayBackNames(map.Exits[0]), null);

		await Assert.That(MapPlan.WayBackNames(map.Exits[0])).IsEqualTo("West;w");
		await Assert.That(Commands(plan.Changes.Single())).Contains(command => command.StartsWith("@open West;w=#0,,#87", StringComparison.Ordinal));
		await Assert.That(MapPlan.OneWay(plan.Preview())).IsEmpty();
	}

	[Test]
	public async Task AWayBackHasNoNameWhenTheExitNamesNoDirection()
	{
		await Assert.That(MapPlan.WayBackNames(new("#88", "Next", [], "#0", "#87", null))).IsEmpty();
		await Assert.That(MapPlan.WayBackNames(new("#90", "Ladder", ["u"], "#0", "#87", null))).IsEqualTo("Down;d");
	}

	[Test]
	public async Task NoWayInNamesOnlyRoomsTheBuilderMayChange()
	{
		var map = Grid(Room("#10", "Quay", [0, 0, 0]), Room("#11", "Shed", [1, 0, 0]), new MapRoomDto("#12", "Theirs", [2, 0, 0], null))
			with
		{ Here = "#10" };

		var stranded = new MapPlan(map).NoWayIn();

		await Assert.That(stranded.Select(room => room.Name)).IsEquivalentTo(["Shed"])
			.Because("the quay is where the builder stands, and the builder does not control Theirs");
	}

	[Test]
	public async Task ExitNamesThatCannotWorkAreRefused()
	{
		await Assert.That(MapPlan.ExitNameProblem("North;n")).IsNull();
		await Assert.That(MapPlan.ExitNameProblem("")).IsEqualTo("empty");
		await Assert.That(MapPlan.ExitNameProblem("North;;n")).IsEqualTo("empty");
		await Assert.That(MapPlan.ExitNameProblem("Inside;in")).IsEqualTo("in");
		await Assert.That(MapPlan.ExitNameProblem("I")).IsEqualTo("in");
		await Assert.That(MapPlan.ExitNameProblem("Gate=open")).IsEqualTo("character");
		await Assert.That(MapPlan.ExitNameProblem("Gate,back")).IsEqualTo("character");
		await Assert.That(MapPlan.RoomNameProblem("Boat, house")).IsNull();
		await Assert.That(MapPlan.RoomNameProblem("[pemit(me,x)]")).IsEqualTo("character");
		await Assert.That(MapPlan.RoomNameProblem(" ")).IsEqualTo("empty");
	}

	[Test]
	public async Task AStepWorkedWhenItsCheckReadsWhatItShould()
	{
		await Assert.That(MapPlan.Worked(new("@dig Shed", "%>", null), "#90:1793148715366")).IsTrue();
		await Assert.That(MapPlan.Worked(new("@dig Shed", "%>", null), "")).IsFalse();
		await Assert.That(MapPlan.Worked(new("@chzone #90=#50", "zone(#90)", "#50"), "#50:1793148715000")).IsTrue();
		await Assert.That(MapPlan.Worked(new("@chzone #90=#50", "zone(#90)", "#50"), "#500:1")).IsFalse();
		await Assert.That(MapPlan.Worked(new("@chzone #90=none", "zone(#90)", "#-1"), "#-1 NO ZONE SET")).IsTrue();
		await Assert.That(MapPlan.Worked(new("&MAP`POS`X #90=4", "get(#90/MAP`POS`X)", "4"), "4")).IsTrue();
	}

	[Test]
	public async Task ADirectionNamesTheExitAndTheWayBack()
	{
		await Assert.That(MapPlan.DirectionNames(1, 0, 0)).IsEqualTo(("East;e", "West;w"));
		await Assert.That(MapPlan.DirectionNames(-1, 1, 0)).IsEqualTo(("Northwest;nw", "Southeast;se"));
		await Assert.That(MapPlan.DirectionNames(0, 0, -1)).IsEqualTo(("Down;d", "Up;u"));
		await Assert.That(MapPlan.DirectionNames(0, 0, 0)).IsNull();
	}

	[Test]
	public async Task ADescriptionIsOneLineWithEachBreakAsPercentR()
	{
		var plan = new MapPlan(Grid(Room("#10", "Quay", [0, 0, 0])));

		plan.Describe("#10", "Tarred planks.  \r\nGulls overhead.\n");

		var step = plan.Changes.Single().Steps.Single();
		await Assert.That(step.Command).IsEqualTo("&DESCRIBE #10=Tarred planks.%rGulls overhead.");
		await Assert.That(step.Check).IsEqualTo("get(#10/DESCRIBE)").Because("& stores the value as typed, so get() reads back the %r");
		await Assert.That(plan.PlannedDescription("#10")).IsEqualTo("Tarred planks.%rGulls overhead.");
	}

	[Test]
	public async Task ARoomThePlanDigsIsDescribedAndRenamedInItsDig()
	{
		var plan = new MapPlan(Grid(Room("#10", "Quay", [0, 0, 0])));
		var key = plan.Dig("Shed", 1, 0, 0, "#10", "East;e", null);

		plan.Describe(key, "Nets.");
		plan.Rename(key, "Net shed");

		var dig = plan.Changes.Single();
		await Assert.That(Commands(dig).First()).IsEqualTo("@dig Net shed");
		await Assert.That(Commands(dig).Last()).IsEqualTo($"&DESCRIBE {{{key}}}=Nets.");
	}

	[Test]
	public async Task RenamingARoomChecksItsNameAndShowsIt()
	{
		var plan = new MapPlan(Grid(Room("#10", "Quay", [0, 0, 0])));

		plan.Rename("#10", " Old quay ");

		var change = plan.Changes.Single();
		await Assert.That(change.Steps.Single()).IsEqualTo(new MapStep("@name #10=Old quay", "name(#10)", "Old quay"));
		await Assert.That((change.Name, change.Other)).IsEqualTo(("Quay", "Old quay"));
		await Assert.That(plan.Preview().Rooms.Single().Name).IsEqualTo("Old quay");
	}

	[Test]
	public async Task AWayBetweenTwoRoomsOnTheMapIsOneOpenCheckedBothWays()
	{
		var plan = new MapPlan(Grid(Room("#10", "Quay", [0, 0, 0]), Room("#11", "Shed", [1, 0, 0])));

		plan.Join("#10", "#11", "East;e", "West;w");

		var step = plan.Changes.Single().Steps.Single();
		await Assert.That(step.Command).IsEqualTo("@open East;e=#11,West;w,#10");
		await Assert.That(step.Check)
			.IsEqualTo("and(strmatch(num(loc(%>)),num(#11)),strmatch(num(loc(first(lexits(#11)))),num(#10)))")
			.Because("the shed has exits of its own already: the way back is the newest of them");
		await Assert.That(plan.Builds).IsEqualTo(2);
		await Assert.That(plan.Preview().Exits.Select(exit => (exit.Name, exit.Source, exit.Destination)))
			.IsEquivalentTo([("East", "#10", "#11"), ("West", "#11", "#10")]);
		await Assert.That(plan.NoWayIn()).IsEmpty();
	}

	[Test]
	public async Task AnExitIsRenamedOrDestroyedButNotBoth()
	{
		var map = Grid(Room("#10", "Quay", [0, 0, 0]), Room("#11", "Shed", [1, 0, 0])) with
		{
			Exits = [new MapExitDto("#30", "East", ["e"], "#10", "#11", null) { Editable = true }],
		};
		var plan = new MapPlan(map);

		plan.RenameExit("#30", "Shed door;door");

		await Assert.That(plan.Changes.Single().Steps.Single()).IsEqualTo(new MapStep("@name #30=Shed door;door", "name(#30)", "Shed door"));
		await Assert.That(plan.Preview().Exits.Single().Aliases).IsEquivalentTo(["door"]);

		plan.RemoveExit("#30");

		var change = plan.Changes.Single();
		await Assert.That(change.Kind).IsEqualTo(MapChangeKind.RemoveExit);
		await Assert.That(change.Steps.Single()).IsEqualTo(new MapStep("@destroy #30", "or(not(valid(#30)),hasflag(#30,GOING))", "1"));
		await Assert.That(plan.Preview().Exits).IsEmpty();
	}

	[Test]
	public async Task ALevelIsNamedOnTheMapObject()
	{
		var plan = new MapPlan(Grid(Room("#10", "Quay", [0, 0, 0])) with { LevelNames = new Dictionary<int, string> { [1] = "Loft" } });

		plan.NameLevel(-1, "Sewers");
		plan.NameLevel(1, "");

		await Assert.That(plan.Changes.Select(change => change.Steps.Single().Command))
			.IsEquivalentTo(["&MAP`LEVEL`-1 #50=Sewers", "&MAP`LEVEL`1 #50="]);
		await Assert.That(plan.Preview().LevelNames).IsEquivalentTo(new Dictionary<int, string> { [-1] = "Sewers" });
	}

	[Test]
	public async Task BuildsCountsEachRoomAndExitThePlanMakes()
	{
		var plan = new MapPlan(Grid(Room("#10", "Quay", [0, 0, 0]), Room("#11", "Shed", [1, 0, 0])));

		var loft = plan.Dig("Loft", 0, 0, 1, "#10", "Up;u", "Down;d");
		plan.Dig("Cellar", 0, 0, -1, "#10", "Down;d", null);
		plan.Join("#11", loft, "Ladder", null);

		await Assert.That(plan.Builds).IsEqualTo(3 + 2 + 1);

		plan.Drop(plan.Changes[0].Key);

		await Assert.That(plan.Builds).IsEqualTo(2).Because("a way to a room no longer dug goes with it");
	}

	[Test]
	public async Task TheFirstRoomOfAnEmptyMapIsDugWithNoExit()
	{
		var plan = new MapPlan(Grid());

		var key = plan.Dig("Quay", 0, 0, 0, null, "", null);

		await Assert.That(Commands(plan.Changes.Single())).IsEquivalentTo(
		[
			"@dig Quay",
			$"@chzone {{{key}}}=#50",
			$"&MAP`COORDS`X {{{key}}}=0",
			$"&MAP`COORDS`Y {{{key}}}=0",
		]);
		await Assert.That(plan.Builds).IsEqualTo(1);
		await Assert.That(plan.Preview().Exits).IsEmpty();
	}

	[Test]
	public async Task AWayToARoomOffTheMapShowsItsName()
	{
		var plan = new MapPlan(Grid(Room("#10", "Quay", [0, 0, 0])));

		plan.Join("#10", "#77", "Gate;g", "Harbour;h", "Town square");

		await Assert.That(plan.Changes.Single().Steps.Single().Command).IsEqualTo("@open Gate;g=#77,Harbour;h,#10");
		await Assert.That(plan.Changes.Single().Other).IsEqualTo("Town square");
		await Assert.That(plan.Preview().Exits.First().DestinationName).IsEqualTo("Town square");
		await Assert.That(plan.Builds).IsEqualTo(2);
	}

	[Test]
	public async Task ClearingATypeShowsWhatTheMapsAboveGiveIt()
	{
		var town = new MapLegendDto("water", "Sea", "info", null) { Inherited = true };
		var plan = new MapPlan(Grid(Room("#10", "Quay", [0, 0, 0])) with
		{
			Legend =
			[
				new("water", "Water", "blue", "~") { Parent = town },
				new("street", "Street", "muted", null) { Inherited = true },
				new("dock", "Dock", null, null),
			],
		});

		plan.SetLegend("water", "", "", "");
		plan.SetLegend("street", "", "", "");
		plan.SetLegend("dock", "", "", "");

		await Assert.That(plan.Preview().Legend).IsEquivalentTo([town, new MapLegendDto("street", "Street", "muted", null) { Inherited = true }])
			.Because("the town's water shows once the map's own is cleared, the street was the town's all along, and no map above names docks");
	}

	[Test]
	public async Task TheMapsOwnSettingsAreItsAttributesAndShowInThePreview()
	{
		var plan = new MapPlan(Grid(Room("#10", "Quay", [0, 0, 0])) with { Legend = [new("water", "Water", "blue", "~")] });

		plan.RenameMap("Old harbour");
		plan.MoveMap("#40", "Coast");
		plan.SetMapSize(12, 8);
		plan.SetDefaultLevel(-1);
		plan.SetLegend("forest", "Old forest", "green", "forest");
		plan.SetLegend("water", "", "", "");

		await Assert.That(plan.Changes.SelectMany(Commands)).IsEquivalentTo(
		[
			"@name #50=Old harbour",
			"@chzone #50=#40",
			"&MAP`SIZE`W #50=12",
			"&MAP`SIZE`H #50=8",
			"&MAP`LEVEL #50=-1",
			"&MAP`LEGEND`FOREST`LABEL #50=Old forest",
			"&MAP`LEGEND`FOREST`TONE #50=green",
			"&MAP`LEGEND`FOREST`ICON #50=forest",
			"&MAP`LEGEND`WATER`LABEL #50=",
			"&MAP`LEGEND`WATER`TONE #50=",
			"&MAP`LEGEND`WATER`ICON #50=",
		]);
		var preview = plan.Preview();
		await Assert.That((preview.Map.Name, preview.Map.Parent, preview.Level)).IsEqualTo(("Old harbour", (string?)"#40", (int?)-1));
		await Assert.That(preview.Size).IsEquivalentTo([12.0, 8.0]);
		await Assert.That(preview.Legend).IsEquivalentTo([new MapLegendDto("forest", "Old forest", "green", "forest")])
			.Because("a type with nothing set is gone from the legend");

		plan.MoveMap(null, null);
		plan.SetBackdrop("https://example.com/isle.png");

		await Assert.That(plan.Changes.Single(change => change.Kind == MapChangeKind.MoveMap).Steps.Single())
			.IsEqualTo(new MapStep("@chzone #50=none", "num(zone(#50))", "#-1"));
		await Assert.That(plan.Preview().Map.Parent).IsNull();
		await Assert.That(plan.Preview().Backdrop).IsEqualTo("https://example.com/isle.png");
	}
	[Test]
	public async Task AWorldMapsPlacesAreChangedWithTheWorldCommands()
	{
		var plan = new MapPlan(Grid() with { Space = new(MapSpaces.Square, 0, 0, 9, 9, [0]) });

		plan.CreateWorld();
		plan.Paint(3, 2, 0, 0, 0, " Forest ");
		plan.Paint(5, 5, 5, 5, 1, "");
		plan.Side(1, 1, 0, "n", "wall", " A fallen oak blocks the path. ");
		plan.Side(1, 1, 0, "n", "open", "");
		plan.NamePlace(4, 4, 0, "Old Oak");
		plan.HidePlace(4, 4, 0, true);
		plan.PlaceRoom(4, 5, 0, "#12");
		plan.OpenWays();

		await Assert.That(plan.Changes.SelectMany(change => change.Steps)).IsEquivalentTo(
		[
			new MapStep("@world/create #50", "worldmap(#50)", "square"),
			new MapStep("@world/paint #50/0,0,0:3,2=Forest", "worldcell(#50,0,0,0)", "forest"),
			new MapStep("@world/paint #50/5,5,1=", "worldcell(#50,5,5,1)", ""),
			new MapStep("@world/wall #50/1,1,0/n=open", "t(match(open none closed,worldway(#50,1,1,0,n)))", "1"),
			new MapStep("@world/place #50/4,4,0=Old Oak", "worldcell(#50,4,4,0,name)", "Old Oak"),
			new MapStep("""@world/data/merge #50/4,4,0={"render":{"hidden":true}}""", """t(strmatch(worldcell(#50,4,4,0,data),*"hidden":true*))""", "1"),
			new MapStep("@world/room #50/4,5,0=#12", "num(worldcell(#50,4,5,0,room))", "#12"),
			new MapStep("+map/exits #50", "gte(words(lexits(#50)),words(worldmap(#50,ways)))", "1"),
		]).Because("a side set twice is one change, the last");
		await Assert.That(plan.Changes.Where(change => change.Kind == MapChangeKind.Paint).Select(change => change.Other))
			.IsEquivalentTo(["0,0,0:3,2", "5,5,1"]);
	}

	[Test]
	public async Task AHexWorldMapIsCreatedAsOne()
	{
		var plan = new MapPlan(Grid());

		plan.CreateWorld(MapSpaces.Hex);

		await Assert.That(plan.Changes.Single().Steps).IsEquivalentTo([new MapStep("@world/create #50=hex", "worldmap(#50)", "hex")]);
	}

	[Test]
	public async Task APointyHexWorldMapIsCreatedAsOne()
	{
		var plan = new MapPlan(Grid());

		plan.CreateWorld(MapSpaces.PointyHex);

		await Assert.That(plan.Changes.Single().Steps)
			.IsEquivalentTo([new MapStep("@world/create #50=pointy-hex", "worldmap(#50)", "pointy-hex")]);
	}

	[Test]
	public async Task AGraphWorldMapIsCreatedAndItsWaysLinkedAndRemoved()
	{
		var plan = new MapPlan(Grid() with { Space = new(MapSpaces.Graph, 0, 0, 9, 9, [0]) });

		plan.CreateWorld(MapSpaces.Graph);
		plan.Link(0, 0, 0, 4, 2, 1, " Old  Road ; road ", "");
		plan.Link(4, 2, 1, 0, 0, 0, "Back", "");
		plan.Unlink(4, 2, 1, "back");

		await Assert.That(plan.Changes.SelectMany(change => change.Steps)).IsEquivalentTo([
			new MapStep("@world/create #50=graph", "worldmap(#50)", "graph"),
			new MapStep("@world/link #50/0,0,0=4,2,1,Old  Road;road", "worldway(#50,0,0,0,old_road,to)", "4,2,1"),
			new MapStep("@world/link #50/4,2,1/back", "worldway(#50,4,2,1,back,to)", ""),
		]).Because("removing a way that was only planned drops the plan to add it");
	}

	[Test]
	public async Task ARoomPlacedByItsObjidIsCheckedByItsDbref()
	{
		var plan = new MapPlan(Grid() with { Space = new(MapSpaces.Square, 0, 0, 9, 9, [0]) });

		plan.PlaceRoom(4, 5, 0, "#12:1793148715366");

		var step = plan.Changes.Single().Steps.Single();
		await Assert.That(step).IsEqualTo(new MapStep("@world/room #50/4,5,0=#12:1793148715366", "num(worldcell(#50,4,5,0,room))", "#12"));
		await Assert.That(MapPlan.Worked(step, "#12")).IsTrue();
	}

	[Test]
	public async Task ARoomStandingOnAPlaceNeedsNoWayIn()
	{
		var map = Grid(Room("#10", "Quay", [0, 0, 0]), Room("#11", "Old forest (1,0,0)", [1, 0, 0]) with { OnPlace = true })
			with
		{ Here = "#10", Space = new(MapSpaces.Square, 0, 0, 9, 9, [0]) };

		await Assert.That(new MapPlan(map).NoWayIn()).IsEmpty().Because("the map's own direction exits lead to a room on a place");
	}
}
