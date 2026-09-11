using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class MoveServiceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMoveService MoveService => WebAppFactoryArg.Services.GetRequiredService<IMoveService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private async Task<AnySharpObject> Node(DBRef dbref)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();

	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private async Task<DBRef> Dig(string prefix)
	{
		var result = await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}"));
		DBRef.TryParse(result.Message!.ToPlainText().Trim(), out var dbref);
		return dbref!.Value;
	}

	[Test]
	public async ValueTask AbsoluteRoomOfSomethingInARoomIsThatRoom()
	{
		var room = await Dig("AbsRoom");
		var thing = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "AbsThing");
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {thing}={room}"));

		var absolute = await MoveService.AbsoluteRoom(await Node(thing));

		await Assert.That(absolute).IsNotNull();
		await Assert.That(absolute!.Object().DBRef).IsEqualTo(room);
	}

	[Test]
	public async ValueTask AbsoluteRoomWalksOutThroughContainers()
	{
		var room = await Dig("NestedRoom");
		var box = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "Box");
		var coin = await TestIsolationHelpers.CreateTestThingAsync(
			GodParser, ConnectionService, "Coin");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {box}={room}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {coin}={box}"));

		var absolute = await MoveService.AbsoluteRoom(await Node(coin));

		await Assert.That(absolute!.Object().DBRef).IsEqualTo(room);
	}

	[Test]
	public async ValueTask AbsoluteRoomOfARoomIsItself()
	{
		var room = await Dig("SelfRoom");
		var absolute = await MoveService.AbsoluteRoom(await Node(room));
		await Assert.That(absolute!.Object().DBRef).IsEqualTo(room);
	}
}
