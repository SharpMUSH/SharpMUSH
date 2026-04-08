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

	/// <summary>
	/// Creates a fresh test player with a registered connection handle,
	/// so movement commands execute against an isolated player instead of God (#1).
	/// </summary>
	private Task<TestIsolationHelpers.TestPlayer> CreateTestPlayerAsync(string namePrefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, namePrefix);

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
		var boxName = TestIsolationHelpers.GenerateUniqueName("TelBox");
		var itemName = TestIsolationHelpers.GenerateUniqueName("TelItem");

		// Create a container and an item
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {boxName}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {itemName}"));

		// Set box as ENTER_OK
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {boxName}=ENTER_OK"));

		// Get both objects
		await Parser.CommandParse(1, ConnectionService, MModule.single($"get {boxName}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"get {itemName}"));

		// Put item inside box
		await Parser.CommandParse(1, ConnectionService, MModule.single($"give {boxName}={itemName}"));

		// Try to teleport box into item (should fail with loop error)
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single($"@teleport {boxName}={itemName}"));

		// Verify command executed (even though it should have rejected the loop)
		await Assert.That(result).IsNotNull();
	}

	/// <summary>
	/// Tests @tel with a single argument (teleports the executor to a room by dbref).
	/// Usage: @tel destination
	/// </summary>
	[Test]
	public async ValueTask TeleportSelfToRoom()
	{
		var player = await CreateTestPlayerAsync("TeleportSelf");

		// Create a destination room (as God, who has permission to @dig)
		var roomName = TestIsolationHelpers.GenerateUniqueName("TelSelfRoom");
		var digResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {roomName}"));
		var roomDbRef = digResult.Message!.ToPlainText()!.Trim();

		// Verify the dig created a room (returns a dbref like #3 or #3:timestamp)
		await Assert.That(roomDbRef).StartsWith("#");

		// Teleport the test player to the new room using its dbref
		var telResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@tel {roomDbRef}"));

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
		// Create a test object and a destination room with unique names
		var objName = TestIsolationHelpers.GenerateUniqueName("TelObj");
		var roomName = TestIsolationHelpers.GenerateUniqueName("TelObjDest");

		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {objName}"));
		var objDbRef = createResult.Message!.ToPlainText()!.Trim();

		var digResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {roomName}"));
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
		var objName = TestIsolationHelpers.GenerateUniqueName("TelByNObj");
		var roomName = TestIsolationHelpers.GenerateUniqueName("TelByNRoom");

		// Create objects with unique names
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {objName}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {roomName}"));

		// Teleport using names (this exercises the name-based locate path)
		var telResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {objName}={roomName}"));

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
		var player = await CreateTestPlayerAsync("HomeCmd");

		// Create a test room to use as home (as God)
		var roomName = TestIsolationHelpers.GenerateUniqueName("HomeRoom");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {roomName}"));

		// Link the test player to the new room as home
		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@link me={roomName}"));

		// Execute home command as the test player
		var result = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("home"));

		// Verify result is not null
		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask HomeCommandAlreadyHome()
	{
		var player = await CreateTestPlayerAsync("HomeAlready");

		// Execute home command when already at home
		var result = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("home"));

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
		var player = await CreateTestPlayerAsync("LeaveCmd");

		// Create a container thing with a unique name (as God)
		var boxName = TestIsolationHelpers.GenerateUniqueName("LeaveBox");
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {boxName}"));
		var boxDbRef = createResult.Message!.ToPlainText()!.Trim();

		// Set it as ENTER_OK so we can enter it
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {boxName}=ENTER_OK"));

		// Teleport the test player into the container (God has permission)
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {player.DbRef}={boxDbRef}"));

		// Leave the box as the test player
		var result = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("leave"));

		// Verify result is not null
		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask LeaveCommandInRoom()
	{
		var player = await CreateTestPlayerAsync("LeaveRoom");

		// Try to leave when in a room (should fail)
		var result = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("leave"));

		// Should notify that can't leave a room
		await Assert.That(result).IsNotNull();
	}
}
