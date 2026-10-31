using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.World;

/// <summary>
/// The World plugin loaded into a real server: <c>@world</c> makes an object a world map and paints, walls, names and
/// fills in its places, and the <c>world…</c> functions read them back.
/// </summary>
[NotInParallel]
public class WorldCommandIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();

	/// <summary>
	/// Runs a command as God and answers what God was told meanwhile that mentions <paramref name="match"/>. Output of an
	/// earlier command can still arrive in that window, so callers look for their line rather than count them.
	/// </summary>
	private async Task<string[]> God(string command, string match)
	{
		var god = ConnectionService.Get(1)!.Ref!.Value;
		await WebAppFactoryArg.QueueBarrierAsync();
		var before = Notifications.CountFor(god);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return [.. Notifications.For(god).Skip(before).Where(line => line.Contains(match, StringComparison.Ordinal))];
	}

	[Test]
	public async Task AWorldMapIsPaintedWalledNamedAndReadBack()
	{
		var map = TestIsolationHelpers.GenerateUniqueName("Wilds");
		await God($"@create {map}", map);

		var created = await God($"@world/create {map}", map);
		await Assert.That(created).Contains($"{map} is now a world map.");
		await Assert.That(created).DoesNotContain($"{map} is already a world map.");
		await Assert.That(await Eval($"get({map}/MAP`KIND)")).IsEqualTo("coordinates");
		await Assert.That(await God($"@world/create {map}", map)).Contains($"{map} is already a world map.");

		await Assert.That(await God($"@world/paint {map}/0,0:9,9=forest", "places")).Contains("Painted 100 places as forest.");
		await God($"&MAP`LEGEND`FOREST`LABEL {map}=Old forest", map);
		await Assert.That(await Eval($"worldcell({map},4,4)/[worldcell({map},4,4,,label)]/[worldcell({map},10,4)]"))
			.IsEqualTo("forest/Old forest/");

		await Assert.That(await God($"@world/wall {map}/4,4/n=wall,A fallen oak blocks the path.", "4,4,0"))
			.Contains("The north side of 4,4,0 is now a wall.");
		await Assert.That(await Eval($"worldway({map},4,4,0,n)/[worldway({map},4,4,0,n,reason)]/[worldway({map},4,5,0,s)]/[worldway({map},4,4,0,ne,to)]"))
			.IsEqualTo("wall/A fallen oak blocks the path./wall/5,5,0");

		await Assert.That(await God($"@world/lock {map}/5,5/e=me", "5,5,0")).Contains("The east side of 5,5,0 is now locked.");
		await Assert.That(await Eval($"worldway({map},5,5,0,e)/[worldway({map},5,5,0,e,state,me)]/[worldway({map},6,5,0,w,state,me)]"))
			.IsEqualTo("lock/open/open");
		await Assert.That(string.Join("\n", await God($"@world {map}/5,5", "East"))).Contains("East: lock, key #1");
		await Assert.That(string.Join("\n", await God($"@world/lock {map}/5,5/e=NoSuchKeyholder", "key")))
			.Contains("I don't understand that key: I can't find a unique object matching NoSuchKeyholder.");
		await Assert.That(await God($"@world/lock {map}/5,5/e", "5,5,0")).Contains("The east side of 5,5,0 is now open.");
		await Assert.That(await Eval($"worldway({map},5,5,0,e)")).IsEqualTo("open");

		await Assert.That(await God($"@world/place {map}/4,4=Old Oak", "4,4,0")).Contains("Named 4,4,0 Old Oak.");
		await Assert.That(await Eval($"worldcell({map},4,4,,name)")).IsEqualTo("Old Oak");

		await Assert.That(await God($$$"""@world/data {{{map}}}/4,4={"render":{"icon":"T"},"info":{"Danger":"Low"}}""", "4,4,0"))
			.Contains("Set the data of 4,4,0.");
		await God($$$"""@world/data/merge {{{map}}}/4,4={"info":null,"note":"[add(1,1)]"}""", "4,4,0");
		await Assert.That(await Eval($"worldcell({map},4,4,,data)")).IsEqualTo("""{"render":{"icon":"T"},"note":"[add(1,1)]"}""");

		await Assert.That(await Eval($"worlddraw({map},1,1,0,1,@)")).IsEqualTo("OOO\nO@O\nOOO");
		await Assert.That(await Eval($"worlddraw({map},0,0,0,1)")).IsEqualTo(" OO\n OO\n   ");
		await Assert.That(await Eval($"worldcell(me,0,0)")).IsEqualTo("#-1 NOT A WORLD MAP");

		// The World map reads the same places through the plugin's map source.
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var number = int.Parse((await Eval($"num({map})")).TrimStart('#'));
		var stored = (await mediator.Send(new GetObjectNodeQuery(new DBRef(number)))).Expect<AnySharpObject>();
		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		var source = WebAppFactoryArg.Services.GetServices<IWorldMapSource>().Single();
		var space = (await source.SpaceAsync(stored, CancellationToken.None)).Expect<MapSpaceDto>();
		await Assert.That((space.Kind, space.Left, space.Bottom, space.Right, space.Top)).IsEqualTo((MapSpaces.Square, 0, 0, 9, 9));
		await Assert.That(space.Levels).IsEquivalentTo([0]);
		var window = await source.CellsAsync(god, stored, new MapWindow(0, 3, 3, 5, 5), CancellationToken.None);
		await Assert.That(window.Cells.Count).IsEqualTo(9);
		var oak = window.Cells.Single(cell => cell is { X: 4, Y: 4 });
		await Assert.That((oak.Type, oak.Name, oak.Icon)).IsEqualTo(("forest", (string?)"Old Oak", (string?)"T"));
		await Assert.That(window.Walls.Select(wall => (wall.X, wall.Y, wall.Side, wall.Kind, wall.Reason)))
			.IsEquivalentTo([(4, 4, "n", MapWallKinds.Wall, (string?)"A fallen oak blocks the path.")]);
	}

	[Test]
	public async Task APointyHexMapIsWalledDrawnAndReadOnItsSixSides()
	{
		var map = TestIsolationHelpers.GenerateUniqueName("Marsh");
		await God($"@create {map}", map);
		await Assert.That(await God($"@world/create {map}=pointy-hex", map)).Contains($"{map} is now a world map.");
		await God($"@world/paint {map}/-2,-2:2,2=grass", "places");
		await Assert.That(await Eval($"worldmap({map})/[worldmap({map},sides)]/[worldmap({map},ways)]"))
			.IsEqualTo("pointy-hex/ne e se sw w nw u d/ne e se sw w nw n s u d");

		await Assert.That(await God($"@world/wall {map}/0,0/w=wall,A dyke.", "0,0,0")).Contains("The west side of 0,0,0 is now a wall.");
		await Assert.That(string.Join("\n", await God($"@world/wall {map}/0,0/n=wall", "sides")))
			.Contains("Which side? A pointy-hex place's sides are ne, e, se, sw, w, nw, u or d");
		await Assert.That(await Eval($"worldway({map},0,0,0,w)/[worldway({map},0,0,0,ne,to)]/[worldway({map},0,0,0,n,to)]"))
			.IsEqualTo("wall/0,1,0/-1,2,0");
		await Assert.That(await Eval($"worldway({map},-1,0,0,e)")).IsEqualTo("wall").Because("the dyke is the same edge seen from the other side");

		await Assert.That(await Eval($"worlddraw({map},0,0,0,1,@)")).IsEqualTo(" G G \nG @ G\n G G ");
	}

	[Test]
	public async Task AGraphMapsPlacesAreJoinedOnlyByTheirNamedWays()
	{
		var map = TestIsolationHelpers.GenerateUniqueName("Downs");
		await God($"@create {map}", map);
		await Assert.That(await God($"@world/create {map}=graph", map)).Contains($"{map} is now a world map.");
		await God($"@world/paint {map}/0,0=grass", "places");
		await God($"@world/paint {map}/4,2,1=grass", "places");
		await Assert.That(await Eval($"worldmap({map})/[worldmap({map},sides)]/[worldmap({map},ways)]")).IsEqualTo("graph//");

		await Assert.That(await God($"@world/link {map}/0,0=4,2,1,Old Road;road,Back Road;back", "leads"))
			.Contains("Old Road leads from 0,0,0 to 4,2,1, and Back Road from 4,2,1 back.");
		await God($"@world/link {map}/4,2,1=9,9,Nowhere", "leads");
		await Assert.That(await Eval($"worldmap({map},ways)/[worldmap({map},names)]/[worldcell({map},0,0,0,ways)]"))
			.IsEqualTo("back_road nowhere old_road/Back Road;back|Nowhere|Old Road;road/old_road");
		await God($"@world/link {map}/4,2,1=0,0,Old Road;old", "leads");
		await Assert.That(await Eval($"worldmap({map},ways)/[worldmap({map},names)]"))
			.IsEqualTo("back_road nowhere old_road/Back Road;back|Nowhere|Old Road;road;old")
			.Because("one exit walks every way by a name, so it goes by all their names");
		await God($"@world/link {map}/4,2,1/old", "Removed");
		await Assert.That(await Eval($"worldway({map},0,0,0,road)/[worldway({map},0,0,0,OLD ROAD,to)]/[worldway({map},4,2,1,back,to)]"))
			.IsEqualTo("open/4,2,1/0,0,0");
		await Assert.That(await Eval($"worldway({map},0,0,0,n)/[worldway({map},0,0,0,n,to)]/[worldway({map},4,2,1,nowhere)]"))
			.IsEqualTo("none//none").Because("a graph map's places have no sides, and nothing is painted at 9,9");
		await Assert.That(await Eval($"worlddraw({map},0,0,0,1,@)")).IsEqualTo("   \n @ \n   ");

		await Assert.That(await God($"@world/wall {map}/4,2,1/back=wall,The bridge is out.", "Back Road"))
			.Contains("The way Back Road out of 4,2,1 is now a wall.");
		await Assert.That(await Eval($"worldway({map},4,2,1,back)/[worldway({map},4,2,1,back,reason)]")).IsEqualTo("wall/The bridge is out.");
		await Assert.That(string.Join("\n", await God($"@world/wall {map}/4,2,1/back=oneway", "already")))
			.Contains("A graph map's way leads one way already; link one back to go both ways.");
		await God($"@world/lock {map}/0,0/road=#FALSE", "locked");
		await Assert.That(await Eval($"worldway({map},0,0,0,road)/[worldway({map},0,0,0,road,state,me)]")).IsEqualTo("lock/lock");
		await Assert.That(string.Join("\n", await God($"@world {map}/0,0", "Way"))).Contains("Way Old Road;road: to 4,2,1, lock, key #FALSE");
		await Assert.That(await God($"@world/lock {map}/0,0/road=", "open")).Contains("The way Old Road out of 0,0,0 is now open.");

		// The World map draws each way from the place it leads out of.
		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var stored = (await mediator.Send(new GetObjectNodeQuery(new DBRef(int.Parse((await Eval($"num({map})")).TrimStart('#'))))))
			.Expect<AnySharpObject>();
		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		var source = WebAppFactoryArg.Services.GetServices<IWorldMapSource>().Single();
		await Assert.That((await source.SpaceAsync(stored, CancellationToken.None)).Expect<MapSpaceDto>().Kind).IsEqualTo(MapSpaces.Graph);
		var window = await source.CellsAsync(god, stored, new MapWindow(1, 0, 0, 9, 9), CancellationToken.None);
		await Assert.That(window.Links.Select(link => (link.X, link.Y, link.Way, link.Names, link.ToX, link.ToY, link.ToZ, link.Kind, link.Reason)))
			.IsEquivalentTo([
				(4, 2, "back_road", "Back Road;back", 0, 0, 0, (string?)MapWallKinds.Wall, (string?)"The bridge is out."),
				(4, 2, "nowhere", "Nowhere", 9, 9, 0, null, null),
			]);
		await Assert.That(window.Walls).IsEmpty();

		await Assert.That(await God($"@world/link {map}/0,0/Old Road", "Removed")).Contains("Removed the way Old Road out of 0,0,0.");
		await Assert.That(await Eval($"worldcell({map},0,0,0,ways)/[worldway({map},4,2,1,back,to)]")).IsEqualTo("/0,0,0")
			.Because("the way back stays");
		await Assert.That(string.Join("\n", await God($"@world/wall {map}/0,0/n=wall", "no way"))).Contains("0,0,0 has no way out named n.");
	}

	[Test]
	public async Task AHexMapIsWalledDrawnAndReadOnItsSixSides()
	{
		var map = TestIsolationHelpers.GenerateUniqueName("Steppe");
		await God($"@create {map}", map);
		await Assert.That(await God($"@world/create {map}=hex", map)).Contains($"{map} is now a world map.");
		await Assert.That(await God($"@world/create {map}=octagon", "stored")).Contains("octagon maps are not stored; a world map is square, hex, pointy-hex or graph.");
		await Assert.That(string.Join("\n", await God($"@world/link {map}/0,0=1,0,Road", "linked")))
			.Contains("Only a graph map's places are linked; give a side to /wall instead.");
		await God($"@world/paint {map}/-2,-2:2,2=grass", "places");
		await Assert.That(await Eval($"worldmap({map})/[worldmap({map},sides)]/[worldmap({map},ways)]/[worldmap({map},levels)]/[worldmap({map},box)]"))
			.IsEqualTo("hex/n ne se s sw nw u d/n ne se s sw nw e w u d/0/-2 -2 2 2");

		await Assert.That(await God($"@world/wall {map}/0,0/sw=wall,A thorn hedge.", "0,0,0"))
			.Contains("The southwest side of 0,0,0 is now a wall.");
		await Assert.That(string.Join("\n", await God($"@world/wall {map}/0,0/e=wall", "sides")))
			.Contains("Which side? A hex place's sides are n, ne, se, s, sw, nw, u or d");
		await Assert.That(await Eval($"worldway({map},0,0,0,sw)/[worldway({map},-1,0,0,ne,reason)]/[worldway({map},0,0,0,se,to)]/[worldway({map},0,0,0,e,to)]/[worldway({map},0,0,0,x)]"))
			.IsEqualTo("wall/A thorn hedge./1,-1,0/2,-1,0/#-1 BAD DIRECTION");
		await Assert.That(string.Join("\n", await God($"@world {map}/-1,0", "Northeast"))).Contains("Northeast: wall (A thorn hedge.)");

		await Assert.That(await Eval($"worlddraw({map},0,0,0,1,@)")).IsEqualTo("  G  \nG   G\n  @  \nG   G\n  G  ");
		// A glyph of two UTF-16 units keeps its place and leaves the others whole.
		await God($"&MAP`LEGEND`GRASS`CHAR {map}=\U0001F33E", map);
		await Assert.That(await Eval($"worlddraw({map},0,0,0,1,@)"))
			.IsEqualTo("  \U0001F33E  \n\U0001F33E   \U0001F33E\n  @  \n\U0001F33E   \U0001F33E\n  \U0001F33E  ");

		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var stored = (await mediator.Send(new GetObjectNodeQuery(new DBRef(int.Parse((await Eval($"num({map})")).TrimStart('#'))))))
			.Expect<AnySharpObject>();
		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		var source = WebAppFactoryArg.Services.GetServices<IWorldMapSource>().Single();
		await Assert.That((await source.SpaceAsync(stored, CancellationToken.None)).Expect<MapSpaceDto>().Kind).IsEqualTo(MapSpaces.Hex);
		var window = await source.CellsAsync(god, stored, new MapWindow(0, 0, 0, 0, 0), CancellationToken.None);
		await Assert.That(window.Walls.Select(wall => (wall.X, wall.Y, wall.Side, wall.Kind)))
			.IsEquivalentTo([(-1, 0, "ne", MapWallKinds.Wall)]).Because("the hedge is stored on the place to the southwest, which borders the window");
	}

	[Test]
	public async Task APlacesDownSideIsReadFromTheLevelBelow()
	{
		var map = TestIsolationHelpers.GenerateUniqueName("Stair");
		await God($"@create {map}", map);
		await God($"@world/create {map}", map);
		await God($"@world/paint {map}/0,0:2,0=stone", "places");
		await God($"@world/paint {map}/0,0,-1:2,0=stone", "places");
		await God($"@world/wall {map}/0,0/d=wall,Bedrock.", "0,0,0");
		await God($"@world/wall {map}/1,0/d=oneway", "1,0,0");
		await God($"@world/wall {map}/2,0,-1/u=lock,A trapdoor.", "2,0,-1");

		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		var stored = (await mediator.Send(new GetObjectNodeQuery(new DBRef(int.Parse((await Eval($"num({map})")).TrimStart('#'))))))
			.Expect<AnySharpObject>();
		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		var source = WebAppFactoryArg.Services.GetServices<IWorldMapSource>().Single();
		async Task<IEnumerable<(int, int, string, string, string?)>> Walls(int level) =>
			(await source.CellsAsync(god, stored, new MapWindow(level, 0, 0, 2, 0), CancellationToken.None))
			.Walls.Select(wall => (wall.X, wall.Y, wall.Side, wall.Kind, wall.Reason));

		await Assert.That(await Walls(0)).IsEquivalentTo([
			(0, 0, "d", MapWallKinds.Wall, (string?)"Bedrock."),
			(1, 0, "d", MapWallKinds.OneWay, (string?)null),
			(2, 0, "d", MapWallKinds.Lock, (string?)"A trapdoor."),
		]);
		await Assert.That(await Walls(-1)).IsEquivalentTo([
			(0, 0, "u", MapWallKinds.Wall, (string?)"Bedrock."),
			(2, 0, "u", MapWallKinds.Lock, (string?)"A trapdoor."),
		]).Because("the way up from below the one-way side is the level above's, as a way down");
	}

	[Test]
	public async Task AHiddenPlaceIsBareTerrainWithoutItsWallsToThoseWhoMayNotExamineTheMap()
	{
		var map = TestIsolationHelpers.GenerateUniqueName("Hollow");
		await God($"@create {map}", map);
		await God($"@world/create {map}", map);
		await God($"@world/paint {map}/0,0:4,4=forest", "places");
		await God($"@world/wall {map}/0,0/n=wall,Brambles.", "0,0,0");
		await God($"@world/wall {map}/2,2/n=wall,The hidden glade's hedge.", "2,2,0");
		await God($"@world/wall {map}/2,1/n=wall,Into the glade.", "2,1,0");
		await God($"@world/wall {map}/1,2/e=wall,Beside the glade.", "1,2,0");
		await God($"@world/place {map}/2,2=Glade", "2,2,0");
		await God($$$"""@world/data {{{map}}}/2,2={"render":{"hidden":true,"icon":"tree"}}""", "2,2,0");

		var mediator = WebAppFactoryArg.Services.GetRequiredService<IMediator>();
		async Task<AnySharpObject> Find(DBRef dbref) =>
			(await mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();
		var stored = await Find(new DBRef(int.Parse((await Eval($"num({map})")).TrimStart('#'))));
		var god = await Find(new DBRef(1));
		var mortal = await Find(await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, mediator, "Walker"));
		var source = WebAppFactoryArg.Services.GetServices<IWorldMapSource>().Single();
		async Task<IEnumerable<(int, int, string)>> Walls(AnySharpObject viewer, int right) =>
			(await source.CellsAsync(viewer, stored, new MapWindow(0, 0, 0, right, 4), CancellationToken.None))
			.Walls.Select(wall => (wall.X, wall.Y, wall.Side));

		await Assert.That(await Walls(god, 4)).IsEquivalentTo([(0, 0, "n"), (2, 2, "n"), (2, 1, "n"), (1, 2, "e")]);
		await Assert.That(await Walls(mortal, 4)).IsEquivalentTo([(0, 0, "n")]);
		// The glade is past the window's right edge, and the wall leading to it still stays hidden.
		await Assert.That(await Walls(mortal, 1)).IsEquivalentTo([(0, 0, "n")]);

		async Task<MapCellDto> Glade(AnySharpObject viewer) =>
			(await source.CellsAsync(viewer, stored, new MapWindow(0, 0, 0, 4, 4), CancellationToken.None)).Cells.Single(cell => cell is { X: 2, Y: 2 });
		await Assert.That((await Glade(god)).Name).IsEqualTo("Glade");
		var bare = await Glade(mortal);
		await Assert.That((bare.Type, bare.Name, bare.Icon)).IsEqualTo(("forest", (string?)null, (string?)null))
			.Because("the glade is drawn as the forest around it, with nothing of the place itself");
		await Assert.That(await Eval($"worldcell({map},2,2,0,name)")).IsEqualTo("Glade").Because("God may examine the map");
		await Assert.That(await source.HiddenAsync(mortal, stored, 2, 2, 0, CancellationToken.None)).IsTrue();
		await Assert.That(await source.HiddenAsync(god, stored, 2, 2, 0, CancellationToken.None)).IsFalse();
		await Assert.That(await source.HiddenAsync(mortal, stored, 1, 2, 0, CancellationToken.None)).IsFalse();
	}
}
