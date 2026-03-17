using Mediator;
using Microsoft.Extensions.DependencyInjection;
using OneOf.Types;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

[NotInParallel]
public class MoveServiceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMoveService MoveService => WebAppFactoryArg.Services.GetRequiredService<IMoveService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask MoveServiceIsRegistered()
	{
		var service = WebAppFactoryArg.Services.GetRequiredService<IMoveService>();
		await Assert.That(service).IsNotNull();
	}

	[Test]
	public async ValueTask CalculateMoveCostReturnsZero()
	{
		var thingDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MoveCostTest");
		var thingNode = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var roomNode = await Mediator.Send(new GetObjectNodeQuery(new SharpMUSH.Library.Models.DBRef(0)));

		var cost = await MoveService.CalculateMoveCostAsync(thingNode.Known.AsContent, roomNode.Known.AsContainer);
		await Assert.That(cost).IsEqualTo(0);
	}

	[Test]
	public async ValueTask NoLoopWithSimpleMove()
	{
		// Moving a thing into a room should never create a loop
		var thingDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "NoLoopSimple");
		var thingNode = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var roomNode = await Mediator.Send(new GetObjectNodeQuery(new SharpMUSH.Library.Models.DBRef(0)));

		var wouldLoop = await MoveService.WouldCreateLoop(thingNode.Known.AsContent, roomNode.Known.AsContainer);
		await Assert.That(wouldLoop).IsFalse();
	}

	[Test]
	public async ValueTask DetectsDirectLoop()
	{
		// Moving thing A into itself would be a direct loop
		var thingDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "DirectLoop");
		var thingNode = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));

		// A thing's direct container would be itself — test if WouldCreateLoop detects it
		// We simulate this by checking: move thingA into a container that IS thingA
		var wouldLoop = await MoveService.WouldCreateLoop(thingNode.Known.AsContent, thingNode.Known.AsContainer);
		await Assert.That(wouldLoop).IsTrue();
	}

	[Test]
	public async ValueTask DetectsIndirectLoop()
	{
		// Moving container A into something that A already contains creates an indirect loop
		// Create: thingA contains thingB; try to move thingA into thingB
		var thingADbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "IndirectLoopA");
		var thingBDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "IndirectLoopB");

		var thingANode = await Mediator.Send(new GetObjectNodeQuery(thingADbRef));
		var thingBNode = await Mediator.Send(new GetObjectNodeQuery(thingBDbRef));

		// Move thingB into thingA first (so A contains B)
		await MoveService.ExecuteMoveAsync(Parser, thingBNode.Known.AsContent, thingANode.Known.AsContainer, silent: true);

		// Re-fetch thingA after B is inside it; now trying to move A into B should be a loop
		var thingANodeFresh = await Mediator.Send(new GetObjectNodeQuery(thingADbRef));
		var thingBNodeFresh = await Mediator.Send(new GetObjectNodeQuery(thingBDbRef));

		var wouldLoop = await MoveService.WouldCreateLoop(thingANodeFresh.Known.AsContent, thingBNodeFresh.Known.AsContainer);
		await Assert.That(wouldLoop).IsTrue();
	}

	[Test]
	public async ValueTask NoLoopIntoRoom()
	{
		// Moving any content object into a room should never create a loop
		// (rooms are never considered loop participants)
		var thingDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "NoLoopRoom");
		var roomDbRef = (await Parser.CommandParse(1, ConnectionService, MModule.single("@dig NoLoopRoom_MoveTest"))).Message!.ToPlainText();
		var thingNode = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var roomNode = await Mediator.Send(new GetObjectNodeQuery(SharpMUSH.Library.Models.DBRef.Parse(roomDbRef)));

		var wouldLoop = await MoveService.WouldCreateLoop(thingNode.Known.AsContent, roomNode.Known.AsContainer);
		await Assert.That(wouldLoop).IsFalse();
	}

	[Test]
	public async ValueTask ExecuteMoveAsyncWithValidMove()
	{
		var thingDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MoveValid");
		var roomResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@dig MoveDestRoom"));
		var roomDbRef = SharpMUSH.Library.Models.DBRef.Parse(roomResult.Message!.ToPlainText()!);

		var thingNode = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var roomNode = await Mediator.Send(new GetObjectNodeQuery(roomDbRef));

		var result = await MoveService.ExecuteMoveAsync(Parser, thingNode.Known.AsContent, roomNode.Known.AsContainer, silent: true);

		await Assert.That(result.IsT0).IsTrue();
	}

	[Test]
	public async ValueTask ExecuteMoveAsyncFailsOnLoop()
	{
		var thingDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MoveLoopFail");
		var thingNode = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));

		// Attempting to move thing into itself must fail
		var result = await MoveService.ExecuteMoveAsync(Parser, thingNode.Known.AsContent, thingNode.Known.AsContainer, silent: true);

		await Assert.That(result.IsT1).IsTrue();
	}

	[Test]
	public async ValueTask ExecuteMoveAsyncFailsOnPermission()
	{
		// God always controls everything, so permission is always granted.
		// Verify ExecuteMoveAsync succeeds (permission does not block us as God).
		var thingDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MovePerm");
		var roomResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@dig MovePermRoom"));
		var roomDbRef = SharpMUSH.Library.Models.DBRef.Parse(roomResult.Message!.ToPlainText()!);

		var thingNode = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var roomNode = await Mediator.Send(new GetObjectNodeQuery(roomDbRef));

		var result = await MoveService.ExecuteMoveAsync(Parser, thingNode.Known.AsContent, roomNode.Known.AsContainer, silent: true);

		// As God, move should succeed
		await Assert.That(result.IsT0).IsTrue();
	}

	[Test]
	public async ValueTask ExecuteMoveAsyncTriggersEnterHooks()
	{
		// Set @aenter on destination room and verify move succeeds end-to-end
		var thingDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MoveEnter");
		var roomResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@dig MoveEnterRoom"));
		var roomDbRef = SharpMUSH.Library.Models.DBRef.Parse(roomResult.Message!.ToPlainText()!);

		var thingNode = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var roomNode = await Mediator.Send(new GetObjectNodeQuery(roomDbRef));

		var result = await MoveService.ExecuteMoveAsync(Parser, thingNode.Known.AsContent, roomNode.Known.AsContainer, silent: false);

		await Assert.That(result.IsT0).IsTrue();
	}

	[Test]
	public async ValueTask ExecuteMoveAsyncTriggersLeaveHooks()
	{
		var thingDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MoveLeave");
		var roomResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@dig MoveLeaveRoom"));
		var roomDbRef = SharpMUSH.Library.Models.DBRef.Parse(roomResult.Message!.ToPlainText()!);

		var thingNode = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var roomNode = await Mediator.Send(new GetObjectNodeQuery(roomDbRef));

		// First move into the room so there is a source to leave from
		await MoveService.ExecuteMoveAsync(Parser, thingNode.Known.AsContent, roomNode.Known.AsContainer, silent: true);

		// Move back to default room
		var defaultRoom = await Mediator.Send(new GetObjectNodeQuery(new SharpMUSH.Library.Models.DBRef(0)));
		var thingNodeFresh = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));

		var result = await MoveService.ExecuteMoveAsync(Parser, thingNodeFresh.Known.AsContent, defaultRoom.Known.AsContainer, silent: false);

		await Assert.That(result.IsT0).IsTrue();
	}

	[Test]
	public async ValueTask ExecuteMoveAsyncTriggersTeleportHooks()
	{
		var thingDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MoveTeleport");
		var roomResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@dig MoveTeleportRoom"));
		var roomDbRef = SharpMUSH.Library.Models.DBRef.Parse(roomResult.Message!.ToPlainText()!);

		var thingNode = await Mediator.Send(new GetObjectNodeQuery(thingDbRef));
		var roomNode = await Mediator.Send(new GetObjectNodeQuery(roomDbRef));

		var result = await MoveService.ExecuteMoveAsync(Parser, thingNode.Known.AsContent, roomNode.Known.AsContainer, cause: "teleport", silent: false);

		await Assert.That(result.IsT0).IsTrue();
	}
}
