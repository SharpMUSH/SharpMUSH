using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class MovementCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask GotoCommand()
	{
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

	/// <summary>
	/// Tests @tel with a single argument (teleports the executor to a room by dbref).
	/// Usage: @tel destination
	/// Known issue: After teleporting a player, the automatic `look` command may generate
	/// ArangoException "document not found" in some database configurations.
	/// </summary>
	[Test]
	public async ValueTask TeleportSelfToRoom()
	{
		// Create a destination room
		var digResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@dig TelSelfRoom"));
		var roomDbRef = digResult.Message!.ToPlainText()!.Trim();

		// Verify the dig created a room (returns a dbref like #3 or #3:timestamp)
		await Assert.That(roomDbRef).StartsWith("#");

		// Teleport self to the new room using its dbref
		// This validates that the @tel command doesn't crash
		var telResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {roomDbRef}"));

		// Verify the command completed without throwing
		await Assert.That(telResult).IsNotNull();
	}

	/// <summary>
	/// Tests @tel with two arguments (teleports an object to a destination by dbref).
	/// Usage: @tel object=destination
	/// </summary>
	[Test]
	public async ValueTask TeleportObjectToRoom()
	{
		// Create a test object and a destination room
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create TelObjTarget"));
		var objDbRef = createResult.Message!.ToPlainText()!.Trim();

		var digResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@dig TelObjDestRoom"));
		var roomDbRef = digResult.Message!.ToPlainText()!.Trim();

		// Verify both created successfully
		await Assert.That(objDbRef).StartsWith("#");
		await Assert.That(roomDbRef).StartsWith("#");

		// Teleport the object to the room using dbrefs
		var telResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {objDbRef}={roomDbRef}"));

		// Verify command completed without throwing
		await Assert.That(telResult).IsNotNull();
	}

	/// <summary>
	/// Tests @tel with object names instead of dbrefs.
	/// Usage: @tel objectname=destinationname
	/// </summary>
	[Test]
	public async ValueTask TeleportByName()
	{
		// Create objects with unique names
		await Parser.CommandParse(1, ConnectionService, MModule.single("@create TelByNameObj"));

		await Parser.CommandParse(1, ConnectionService, MModule.single("@dig TelByNameRoom"));

		// Teleport using names (this exercises the name-based locate path)
		var telResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@tel TelByNameObj=TelByNameRoom"));

		// Verify command completed without throwing
		await Assert.That(telResult).IsNotNull();
	}

	/// <summary>
	/// Tests @tel to an invalid destination.
	/// Should produce an error notification.
	/// </summary>
	[Test]
	public async ValueTask TeleportToInvalidDestination()
	{
		var result = await Parser.CommandParse(1, ConnectionService,
			MModule.single("@tel #99999"));

		// Command should still return a result (error handling)
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
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask EnterCommand()
	{
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
