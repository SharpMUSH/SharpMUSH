using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.World;

/// <summary>
/// The rooms on a world map, with the World plugin loaded into a real server: <c>worldroom()</c> lends a place a
/// room, <c>@world/sweep</c> takes back the empty ones as spares for the next place, and <c>@world/room</c> places a
/// room to stay.
/// </summary>
[NotInParallel]
public class WorldRoomIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();

	/// <summary>Runs a command as God and answers what God was told meanwhile that mentions <paramref name="match"/>.</summary>
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
	public async Task APlaceIsLentARoomThatGoesBackOnceEmptyAndServesTheNextPlace()
	{
		var map = TestIsolationHelpers.GenerateUniqueName("Meadows");
		await God($"@create {map}", map);
		await God($"@world/create {map}", map);
		await God($"@world/paint {map}/0,0:2,2=grass", "places");
		await God($"&MAP`LEGEND`GRASS`LABEL {map}=Meadow", map);
		await God($"@world/place {map}/1,1=Old Well", "1,1,0");

		var well = await Eval($"worldroom({map},1,1)");
		await Assert.That(well).StartsWith("#");
		await Assert.That(await Eval($"worldroom({map},1,1,0)/[worldroom({map},worldway({map},1,0,0,n,to))]")).IsEqualTo($"{well}/{well}");
		await Assert.That(await Eval($"name({well})/[num(zone({well}))]/[hasflag({well},FLOATING)]")).IsEqualTo($"Old Well/{await Eval($"num({map})")}/1");
		await Assert.That(await Eval($"get({well}/MAP`PLACE)")).IsEqualTo(await Eval($"worldcell({map},1,1,,id)"));
		await Assert.That(await Eval($"worldcell({map},1,1,,room)/[worldcell({map},1,1,,permanent)]/[worldcell({map},1,0,,room)]"))
			.IsEqualTo($"{well}/0/");
		await Assert.That(await Eval($"worldplace({well})/[worldplace({well},map)]/[worldplace({well},permanent)]"))
			.IsEqualTo($"1,1,0/{await Eval($"num({map})")}/0");
		await Assert.That(await Eval($"name(worldroom({map},0,0))")).IsEqualTo("Meadow (0,0,0)");
		await Assert.That(await Eval($"worldroom({map},5,5)")).IsEqualTo("#-1 NOTHING THERE");
		await Assert.That(await Eval($"worldplace(me)")).IsEqualTo("#-1 NOT A ROOM");

		// A room holding something stays lent; an empty one goes back, tidied, and is the next place's room.
		var corner = await Eval($"worldroom({map},0,0)");
		var stone = TestIsolationHelpers.GenerateUniqueName("Stone");
		await God($"@create {stone}", stone);
		await God($"@tel {stone}={corner}", stone);
		await Assert.That(await God("@world/sweep", "Took back")).IsNotEmpty();
		await Assert.That(await Eval($"worldcell({map},0,0,,room)/[worldcell({map},1,1,,room)]")).IsEqualTo($"{corner}/");
		await Assert.That(await Eval($"name({well})/[zone({well})]/[get({well}/MAP`PLACE)]")).IsEqualTo("Spare room/#-1 NO ZONE SET/");

		// The sweep is the game's, so other tests' rooms may be spares too, and one of them may be older; the next place
		// is lent some spare, made before this marker, not a new room.
		var marker = TestIsolationHelpers.GenerateUniqueName("Marker");
		await God($"@create {marker}", marker);
		var next = await Eval($"worldroom({map},2,2)");
		await Assert.That(await Eval($"lte(after(objid({next}),:),after(objid(num(search(name={marker}))),:))")).IsEqualTo("1")
			.Because("a spare is lent before a room is made");
		await Assert.That(await Eval($"name({next})/[worldplace({next})]")).IsEqualTo("Meadow (2,2,0)/2,2,0");
	}

	[Test]
	public async Task ARoomLetGoWhileSomethingIsInItWaitsUntilItIsEmpty()
	{
		var map = TestIsolationHelpers.GenerateUniqueName("Marsh");
		await God($"@create {map}", map);
		var mapRef = await Eval($"num({map})");
		await God($"@world/create {mapRef}", map);
		await God($"@world/paint {mapRef}/0,0=reeds", "places");
		var room = await Eval($"worldroom({mapRef},0,0)");
		var heron = TestIsolationHelpers.GenerateUniqueName("Heron");
		await God($"@create {heron}", heron);
		var heronRef = await Eval($"num({heron})");
		await God($"@tel {heronRef}={room}", heron);

		// What purging the map's recover entry does, without a purge pass over every test's objects.
		if (!DBRef.TryParse(await Eval($"objid({mapRef})"), out var objid) || objid is null)
			throw new InvalidOperationException($"No objid for {mapRef}.");
		foreach (var cleanup in WebAppFactoryArg.Services.GetServices<IFreedObjectCleanup>())
		{
			await cleanup.ObjectsFreedAsync([objid.Value]);
		}
		await Assert.That(await Eval($"worldplace({room})")).IsEmpty().Because("a destroyed map lets go of its rooms");
		await God("@world/sweep", "Took back");
		await Assert.That(await Eval($"name({room})")).IsEqualTo("reeds (0,0,0)").Because("a room holding something is not made spare");

		await God($"@tel {heronRef}=#0", heron);
		await God("@world/sweep", "Took back");
		await Assert.That(await Eval($"name({room})")).IsEqualTo("Spare room");
	}

	[Test]
	public async Task ARoomPlacedToStayIsTheOnlyRoomItsPlaceHas()
	{
		var map = TestIsolationHelpers.GenerateUniqueName("Hamlet");
		await God($"@create {map}", map);
		await God($"@world/create {map}", map);
		await God($"@world/paint {map}/0,0:2,2=road", "places");
		var inn = TestIsolationHelpers.GenerateUniqueName("Inn");
		await God($"@dig {inn}", inn);
		var innRef = await Eval($"num(search(name={inn}))");

		await Assert.That(await God($"@world/room {map}/1,1={innRef}", inn)).Contains($"{inn} now stands on 1,1,0 to stay.");
		await Assert.That(await Eval($"worldroom({map},1,1)/[worldcell({map},1,1,,permanent)]/[worldplace({innRef},permanent)]"))
			.IsEqualTo($"{innRef}/1/1");
		await Assert.That(await Eval($"num(zone({innRef}))")).IsEqualTo(await Eval($"num({map})"));
		await Assert.That(await Eval($"get({innRef}/MAP`PLACE)")).IsEqualTo(await Eval($"worldcell({map},1,1,,id)"));
		await Assert.That(string.Join("\n", await God($"@world {map}/1,1", "Room:"))).Contains($"Room: {innRef}, to stay");

		await Assert.That(await God($"@world/room {map}/1,1=", "1,1,0")).Contains($"Took {innRef} off 1,1,0.");
		await Assert.That(await Eval($"get({innRef}/MAP`PLACE)/[worldplace({innRef})]")).IsEqualTo("/");

		// Keeping a lent room: the same room, placed to stay where it stands.
		var lent = await Eval($"worldroom({map},2,1)");
		await Assert.That(await God($"@world/room {map}/2,1={lent}", "2,1,0")).Contains($"{await Eval($"name({lent})")} stays on 2,1,0 now.");
		await Assert.That(await Eval($"worldcell({map},2,1,,permanent)/[worldroom({map},2,1)]")).IsEqualTo($"1/{lent}");
		await Assert.That(await God($"@world/room {map}/2,1={innRef}", "2,1,0")).Contains("Another room stands on 2,1,0 to stay.");
	}
}
