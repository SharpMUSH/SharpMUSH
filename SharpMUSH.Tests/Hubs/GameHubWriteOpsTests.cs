using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Hubs;

/// <summary>
/// Unit tests for the server-side write operations added to <see cref="GameHub"/>:
/// <see cref="GameHub.SendToCharacterAsync"/>, <see cref="GameHub.SendToRoomAsync"/>,
/// and <see cref="GameHub.BroadcastSystemMessageAsync"/>.
/// </summary>
public class GameHubWriteOpsTests
{
	private static (
			IHubContext<GameHub, IGameHubClient> hubContext,
			IGameHubClient groupClient,
			IHubClients<IGameHubClient> clients)
	BuildHubContext()
	{
		var groupClient = Substitute.For<IGameHubClient>();
		groupClient.ReceiveOutput(Arg.Any<GameOutputMessage>()).Returns(Task.CompletedTask);
		groupClient.ReceiveRoomEvent(Arg.Any<RoomEventMessage>()).Returns(Task.CompletedTask);

		var allClient = Substitute.For<IGameHubClient>();
		allClient.ReceiveOutput(Arg.Any<GameOutputMessage>()).Returns(Task.CompletedTask);

		var clients = Substitute.For<IHubClients<IGameHubClient>>();
		clients.Group(Arg.Any<string>()).Returns(groupClient);
		clients.All.Returns(allClient);

		var hubContext = Substitute.For<IHubContext<GameHub, IGameHubClient>>();
		hubContext.Clients.Returns(clients);

		return (hubContext, groupClient, clients);
	}

	[Test]
	public async Task SendToCharacterAsync_CallsCorrectGroup()
	{
		var (ctx, _, clients) = BuildHubContext();
		var msg = new GameOutputMessage("#42", "Hello", DateTimeOffset.UtcNow, MessageType.Normal);

		await GameHub.SendToCharacterAsync(ctx, new DBRef(42), msg);

		clients.Received(1).Group("char:#42");
	}

	[Test]
	public async Task SendToCharacterAsync_InvokesReceiveOutput()
	{
		var (ctx, groupClient, _) = BuildHubContext();
		var msg = new GameOutputMessage("#7", "Hello", DateTimeOffset.UtcNow, MessageType.Normal);

		await GameHub.SendToCharacterAsync(ctx, new DBRef(7), msg);

		await groupClient.Received(1).ReceiveOutput(msg);
	}

	[Test]
	public async Task SendToCharacterAsync_DifferentDbrefs_UseDistinctGroups()
	{
		var (ctx, _, clients) = BuildHubContext();
		var msg = new GameOutputMessage("#1", "hi", DateTimeOffset.UtcNow, MessageType.Normal);

		await GameHub.SendToCharacterAsync(ctx, new DBRef(1), msg);
		await GameHub.SendToCharacterAsync(ctx, new DBRef(2), msg);

		clients.Received(1).Group("char:#1");
		clients.Received(1).Group("char:#2");
	}

	[Test]
	public async Task SendToRoomAsyncUsesSharedRecipientDispatcher()
	{
		var dispatcher = Substitute.For<IRoomEventDispatcher>();
		var message = new RoomEventMessage("#5:1", RoomEventType.Say, "Actor", "Hello", "#7:1");
		await GameHub.SendToRoomAsync(dispatcher, new DBRef(5, 1), message);
		await dispatcher.Received(1).DispatchAsync(message, Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task SendToRoomAsyncRejectsMismatchedRoom()
	{
		var dispatcher = Substitute.For<IRoomEventDispatcher>();
		var message = new RoomEventMessage("#5:1", RoomEventType.Say, "Actor", "Hello", "#7:1");
		await GameHub.SendToRoomAsync(dispatcher, new DBRef(6, 1), message);
		await dispatcher.DidNotReceive().DispatchAsync(Arg.Any<RoomEventMessage>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task BroadcastSystemMessageAsync_InvokesAll()
	{
		var allClient = Substitute.For<IGameHubClient>();
		allClient.ReceiveOutput(Arg.Any<GameOutputMessage>()).Returns(Task.CompletedTask);

		var clients = Substitute.For<IHubClients<IGameHubClient>>();
		clients.All.Returns(allClient);

		var ctx = Substitute.For<IHubContext<GameHub, IGameHubClient>>();
		ctx.Clients.Returns(clients);

		await GameHub.BroadcastSystemMessageAsync(ctx, "Server restart in 5 minutes.");

		await allClient.Received(1).ReceiveOutput(
				Arg.Is<GameOutputMessage>(m =>
						m.CharacterDbref == null &&
						m.Content == "Server restart in 5 minutes." &&
						m.MessageType == MessageType.System));
	}

	[Test]
	public async Task BroadcastSystemMessageAsync_HasNoRecipientCharacter()
	{
		var allClient = Substitute.For<IGameHubClient>();
		allClient.ReceiveOutput(Arg.Any<GameOutputMessage>()).Returns(Task.CompletedTask);

		var clients = Substitute.For<IHubClients<IGameHubClient>>();
		clients.All.Returns(allClient);

		var ctx = Substitute.For<IHubContext<GameHub, IGameHubClient>>();
		ctx.Clients.Returns(clients);

		await GameHub.BroadcastSystemMessageAsync(ctx, "Hello all.");

		await allClient.Received(1).ReceiveOutput(
				Arg.Is<GameOutputMessage>(m => m.CharacterDbref == null && m.MessageType == MessageType.System));
	}
}
