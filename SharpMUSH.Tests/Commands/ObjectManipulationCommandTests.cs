using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class ObjectManipulationCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async ValueTask GetCommand()
	{
		// Create a thing in the room
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@create GetTestObject"));
		var thingDbRef = DBRef.Parse(result.Message!.ToPlainText()!);

		// Get the thing
		await Parser.CommandParse(1, ConnectionService, MModule.single("get GetTestObject"));

		// Should receive messages including "Taken."
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("Taken")));
	}

	[Test]
	public async ValueTask GetFromContainer()
	{
		// Create a container and an object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Container2"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create InnerObject2"));
		
		// Set container as ENTER_OK
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set Container2=ENTER_OK"));
		
		// Put inner object in container
		await Parser.CommandParse(1, ConnectionService, MModule.single("get InnerObject2"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("give Container2=InnerObject2"));
		
		// Try to get from container
		await Parser.CommandParse(1, ConnectionService, MModule.single("get Container2's InnerObject2"));

		// Should receive messages including "Taken."
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("Taken")));
	}

	[Test]
	public async ValueTask GetNonexistentObject()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("get NonexistentObject12345"));

		// Should receive error message
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("don't see")));
	}

	[Test]
	public async ValueTask DropCommand()
	{
		// Create and get an object
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create DropTestObject"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("get DropTestObject"));

		// Drop it
		await Parser.CommandParse(1, ConnectionService, MModule.single("drop DropTestObject"));

		// Should receive messages including "Dropped."
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("Dropped")));
	}

	[Test]
	public async ValueTask GiveCommand()
	{
		// Create a thing and another player
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create GiveTestObject"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("get GiveTestObject"));
		
		// Create a recipient (needs to be created as player or thing with ENTER_OK)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Recipient"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set Recipient=ENTER_OK"));

		// Give the object
		await Parser.CommandParse(1, ConnectionService, MModule.single("give Recipient=GiveTestObject"));

		// Should receive messages including "Given."
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("Given")));
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask UseCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("use test object"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask InventoryCommand()
	{
		// Just test the command runs
		await Parser.CommandParse(1, ConnectionService, MModule.single("inventory"));

		// Should receive some notification about inventory
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask GetPreventsLoops()
	{
		// Create a box and a bag
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Box"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Bag"));
		
		// Set both as ENTER_OK
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set Box=ENTER_OK"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set Bag=ENTER_OK"));
		
		// Get both objects
		await Parser.CommandParse(1, ConnectionService, MModule.single("get Box"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("get Bag"));
		
		// Put Bag inside Box
		await Parser.CommandParse(1, ConnectionService, MModule.single("give Box=Bag"));
		
		// Try to get Box from inside Bag (should fail with loop error)
		await Parser.CommandParse(1, ConnectionService, MModule.single("get Bag's Box"));
		
		// Should receive error about containment loop
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("loop")));
	}

	[Test]
	public async ValueTask GivePreventsLoops()
	{
		// Create a chest and a sack
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Chest"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create Sack"));
		
		// Set both as ENTER_OK
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set Chest=ENTER_OK"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set Sack=ENTER_OK"));
		
		// Get both objects
		await Parser.CommandParse(1, ConnectionService, MModule.single("get Chest"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("get Sack"));
		
		// Put Sack inside Chest
		await Parser.CommandParse(1, ConnectionService, MModule.single("give Chest=Sack"));
		
		// Try to give Chest to Sack (should fail with loop error)
		await Parser.CommandParse(1, ConnectionService, MModule.single("give Sack=Chest"));
		
		// Should receive error about containment loop
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("loop")));
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask DestroyCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@destroy #100"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask NukeCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@nuke #100"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask UndestroyCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@undestroy #100"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}
}
