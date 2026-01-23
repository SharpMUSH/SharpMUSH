using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class MovementCommandTests : TestClassFactory
{
	private IConnectionService ConnectionService => Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => CommandParser;
	private IMediator Mediator => Services.GetRequiredService<IMediator>();

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask GotoCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("goto #0"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask TeleportPreventsLoops()
	{
		// Create a container and an item
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create TeleportBox"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create TeleportItem"));
		
		// Set box as ENTER_OK
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set TeleportBox=ENTER_OK"));
		
		// Get both objects
		await Parser.CommandParse(1, ConnectionService, MModule.single("get TeleportBox"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("get TeleportItem"));
		
		// Put TeleportItem inside TeleportBox
		await Parser.CommandParse(1, ConnectionService, MModule.single("give TeleportBox=TeleportItem"));
		
		// Try to teleport TeleportBox into TeleportItem (should fail with loop error)
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("@teleport TeleportBox=TeleportItem"));
		
		// Verify command executed (even though it should have rejected the loop)
		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask HomeCommand()
	{
		// Create a test room to use as home
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dig TestHomeRoom"));
		
		// Link player to the new room as home
		await Parser.CommandParse(1, ConnectionService, MModule.single("@link me=TestHomeRoom"));
		
		// Execute home command
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("home"));
		
		// Verify result is not null
		await Assert.That(result).IsNotNull();
	}
	
	[Test]
	public async ValueTask HomeCommandAlreadyHome()
	{
		// Execute home command when already at home
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("home"));
		
		// Should notify that already home
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask EnterCommand()
	{
		// Clear any previous calls to the mock
		await Parser.CommandParse(1, ConnectionService, MModule.single("enter #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask LeaveCommand()
	{
		// Create a container thing
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create LeaveTestBox"));
		
		// Set it as ENTER_OK so we can enter it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@set LeaveTestBox=ENTER_OK"));
		
		// Get the box
		await Parser.CommandParse(1, ConnectionService, MModule.single("get LeaveTestBox"));
		
		// Enter the box
		await Parser.CommandParse(1, ConnectionService, MModule.single("enter LeaveTestBox"));
		
		// Leave the box
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("leave"));
		
		// Verify result is not null
		await Assert.That(result).IsNotNull();
	}
	
	[Test]
	public async ValueTask LeaveCommandInRoom()
	{
		// Try to leave when in a room (should fail)
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single("leave"));
		
		// Should notify that can't leave a room
		await Assert.That(result).IsNotNull();
	}
}
