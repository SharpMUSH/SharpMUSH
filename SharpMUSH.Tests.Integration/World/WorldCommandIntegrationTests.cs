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
		var space = (await source.SpaceAsync(stored, CancellationToken.None))!;
		await Assert.That((space.Kind, space.Left, space.Bottom, space.Right, space.Top)).IsEqualTo((MapSpaces.Square, 0, 0, 9, 9));
		await Assert.That(space.Levels).IsEquivalentTo([0]);
		var window = await source.CellsAsync(god, stored, new MapWindow(0, 3, 3, 5, 5), CancellationToken.None);
		await Assert.That(window.Cells.Count).IsEqualTo(9);
		var oak = window.Cells.Single(cell => cell is { X: 4, Y: 4 });
		await Assert.That((oak.Type, oak.Name, oak.Icon)).IsEqualTo(("forest", (string?)"Old Oak", (string?)"T"));
		await Assert.That(window.Walls.Select(wall => (wall.X, wall.Y, wall.Side, wall.Kind, wall.Reason)))
			.IsEquivalentTo([(4, 4, "n", MapWallKinds.Wall, (string?)"A fallen oak blocks the path.")]);
	}
}
