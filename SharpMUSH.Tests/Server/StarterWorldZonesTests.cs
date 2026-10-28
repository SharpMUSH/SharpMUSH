using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// The starter world's OOC and IC zones, made over rooms of the test's own: each zone is a compass map whose legend
/// draws its rooms, and the way to the Commons lets through only a character <c>isapproved()</c> passes.
/// </summary>
public class StarterWorldZonesTests : ServerTestBase
{
	private StarterWorldService StarterWorld => WebAppFactoryArg.Services.GetRequiredService<StarterWorldService>();

	[Test]
	public async Task TheCommonsIsInCharacterAndOnlyApprovedCharactersMayGoThere()
	{
		var arrival = (await Cmd($"@dig {TestIsolationHelpers.GenerateUniqueName("Arrival")}")).Split(':')[0];
		var tutorial = (await Cmd($"@dig {TestIsolationHelpers.GenerateUniqueName("Tutorial")}")).Split(':')[0];

		var failures = await StarterWorld.ZonesAsync(arrival, tutorial, CancellationToken.None);

		await Assert.That(failures).IsEmpty();
		var ooc = await Eval($"num(zone({arrival}))");
		var way = await Eval($"num(lexits({arrival}))");
		var commons = await Eval($"num(loc({way}))");
		var ic = await Eval($"num(zone({commons}))");
		await Assert.That(await Eval($"name({ooc}) [num(zone({tutorial}))] [get({ooc}/MAP`KIND)] [get({arrival}/ENVIRONMENT)]"))
			.IsEqualTo($"OOC {ooc} compass ooc");
		await Assert.That(await Eval($"name({ic}) [name({commons})] [get({ic}/MAP`KIND)] [get({commons}/ENVIRONMENT)]"))
			.IsEqualTo("IC Commons compass ic");
		await Assert.That(await Eval($"[get({ic}/MAP`LEGEND`IC`LABEL)]|[get({ic}/MAP`LEGEND`IC`TONE)]|[get({ic}/MAP`LEGEND`IC`ICON)]"))
			.IsEqualTo("In character|primary|mask");
		await Assert.That(await Eval($"[name({way})] [num(loc(lexits({commons})))]")).IsEqualTo($"In Character {arrival}");

		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		var map = (await ActivatorUtilities.CreateInstance<WorldMapReader>(WebAppFactoryArg.Services)
			.ReadAsync(god, int.Parse(ic[1..], CultureInfo.InvariantCulture), CancellationToken.None)).Expect<MapDto>();
		await Assert.That(map.Rooms.Select(room => (room.Id, room.Environment))).IsEquivalentTo([(commons, (string?)"ic")]);
		await Assert.That(map.Legend).IsEquivalentTo([new MapLegendDto("ic", "In character", "primary", "mask")]);

		var player = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "Newcomer");
		await Assert.That(await Eval($"elock({way},{player})")).IsEqualTo("0");

		await Cmd($"@role/assign {player}=approved");

		await Assert.That(await Eval($"elock({way},{player})")).IsEqualTo("1");
	}
}
