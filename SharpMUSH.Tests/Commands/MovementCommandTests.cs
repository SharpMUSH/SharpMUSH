using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

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
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("goto #0"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "You can't go that way.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask TeleportPreventsLoops()
	{
		var boxName = TestIsolationHelpers.GenerateUniqueName("TelBox");
		var itemName = TestIsolationHelpers.GenerateUniqueName("TelItem");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {boxName}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {itemName}"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {boxName}=ENTER_OK"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"get {boxName}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"get {itemName}"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"give {boxName}={itemName}"));

		var result = await Parser.CommandParse(1, ConnectionService, MModule.single($"@teleport {boxName}={itemName}"));

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

		var roomName = TestIsolationHelpers.GenerateUniqueName("TelSelfRoom");
		var digResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {roomName}"));
		var roomDbRef = digResult.Message!.ToPlainText()!.Trim();

		await Assert.That(roomDbRef).StartsWith("#");

		var telResult = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@tel {roomDbRef}"));

		await Assert.That(telResult).IsNotNull();
	}

	/// <summary>
	/// Tests @tel with two arguments (teleports an object to a destination by dbref).
	/// Usage: @tel object=destination
	/// </summary>
	[Test]
	public async ValueTask TeleportObjectToRoom()
	{
		var objName = TestIsolationHelpers.GenerateUniqueName("TelObj");
		var roomName = TestIsolationHelpers.GenerateUniqueName("TelObjDest");

		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {objName}"));
		var objDbRef = createResult.Message!.ToPlainText()!.Trim();

		var digResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {roomName}"));
		var roomDbRef = digResult.Message!.ToPlainText()!.Trim();

		await Assert.That(objDbRef).StartsWith("#");
		await Assert.That(roomDbRef).StartsWith("#");

		var telResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {objDbRef}={roomDbRef}"));

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

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {objName}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {roomName}"));

		// Teleport using names (this exercises the name-based locate path)
		var telResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {objName}={roomName}"));

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

		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask HomeCommand()
	{
		var player = await CreateTestPlayerAsync("HomeCmd");

		var roomName = TestIsolationHelpers.GenerateUniqueName("HomeRoom");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@dig {roomName}"));

		await Parser.CommandParse(player.Handle, ConnectionService, MModule.single($"@link me={roomName}"));

		var result = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("home"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask HomeCommandAlreadyHome()
	{
		var player = await CreateTestPlayerAsync("HomeAlready");

		var result = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("home"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask EnterCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("enter #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), "You can't enter that.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask LeaveCommand()
	{
		var player = await CreateTestPlayerAsync("LeaveCmd");

		var boxName = TestIsolationHelpers.GenerateUniqueName("LeaveBox");
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {boxName}"));
		var boxDbRef = createResult.Message!.ToPlainText()!.Trim();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {boxName}=ENTER_OK"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {player.DbRef}={boxDbRef}"));

		var result = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("leave"));

		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async ValueTask LeaveCommandInRoom()
	{
		var player = await CreateTestPlayerAsync("LeaveRoom");

		var result = await Parser.CommandParse(player.Handle, ConnectionService, MModule.single("leave"));

		await Assert.That(result).IsNotNull();
	}
}
