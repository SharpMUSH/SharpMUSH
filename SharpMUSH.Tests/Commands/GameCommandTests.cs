using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class GameCommandTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private INotifyService NotifyService => Factory.NotifyService;
	private IConnectionService ConnectionService => Factory.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => Factory.CommandParser;

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask BuyCommand()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single("buy sword"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask ScoreCommand()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single("score"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask TeachCommand()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single("teach #1=skill"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask FollowCommand()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single("follow #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask UnfollowCommand()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single("unfollow"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask DesertCommand()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single("desert"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask DismissCommand()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single("dismiss #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask EmptyCommand()
	{
		// Create a container and some items to put in it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create EmptyTestContainer"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create EmptyTestItem1"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create EmptyTestItem2"));
		
		// Set container as ENTER_OK so we can access its contents
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set EmptyTestContainer=ENTER_OK"));
		
		// Get the container
		await Parser.CommandParse(1, ConnectionService, MModule.single("get EmptyTestContainer"));
		
		// Put items inside the container
		await Parser.CommandParse(1, ConnectionService, MModule.single("give EmptyTestContainer=EmptyTestItem1"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("give EmptyTestContainer=EmptyTestItem2"));
		
		// Empty the container
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("empty EmptyTestContainer"));
		
		// Verify result is not null
		await Assert.That(result).IsNotNull();
	}
	
	[Test]
	public async ValueTask EmptyCommandSameLocation()
	{
		// Create a container and an item in the same room
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create EmptyTestBox"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create EmptyTestThing"));
		
		// Set box as ENTER_OK
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set EmptyTestBox=ENTER_OK"));
		
		// Put thing inside the box
		await Parser.CommandParse(1, ConnectionService, MModule.single("give EmptyTestBox=EmptyTestThing"));
		
		// Drop the box in the room
		await Parser.CommandParse(1, ConnectionService, MModule.single("drop EmptyTestBox"));
		
		// Empty the box (should move item from box to room, passing through our hands)
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("empty EmptyTestBox"));
		
		// Verify result is not null
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask WithCommand()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService, MModule.single("with #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
