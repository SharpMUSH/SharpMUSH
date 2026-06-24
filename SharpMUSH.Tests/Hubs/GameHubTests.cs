using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Tests.Hubs;

/// <summary>
/// Unit tests for <see cref="GameHub"/>.
/// Uses NSubstitute to mock IHubContext infrastructure; no live SignalR connection needed.
/// </summary>
public class GameHubTests
{
	private static (GameHub hub, IGroupManager groups) BuildHub(string? characterDbref = "42")
	{
		var (hub, groups, _) = BuildHubWithBus(characterDbref);
		return (hub, groups);
	}

	private static (GameHub hub, IGroupManager groups, IMessageBus bus) BuildHubWithBus(string? characterDbref = "42")
	{
		var groups = Substitute.For<IGroupManager>();
		groups.AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.CompletedTask);
		groups.RemoveFromGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.CompletedTask);

		var clients = Substitute.For<IHubCallerClients<IGameHubClient>>();
		var context = Substitute.For<HubCallerContext>();
		context.ConnectionId.Returns("conn-001");

		if (characterDbref is not null)
		{
			var identity = new ClaimsIdentity(
				new[] { new Claim(GameHub.CharacterDbrefClaim, characterDbref) },
				"TestAuth");
			context.User.Returns(new ClaimsPrincipal(identity));
		}
		else
		{
			context.User.Returns(new ClaimsPrincipal(new ClaimsIdentity()));
		}

		var bus = Substitute.For<IMessageBus>();
		bus.Publish(Arg.Any<GameCommandMessage>(), Arg.Any<CancellationToken>())
			.Returns(Task.CompletedTask);

		var hub = new GameHub(bus, NullLogger<GameHub>.Instance)
		{
			Groups = groups,
			Clients = clients,
			Context = context,
		};

		return (hub, groups, bus);
	}

	[Test]
	public async Task OnConnectedAsync_WithCharacterDbrefClaim_AddsToCharacterGroup()
	{
		var (hub, groups) = BuildHub("99");

		await hub.OnConnectedAsync();

		await groups.Received(1).AddToGroupAsync(
			"conn-001",
			"char:99",
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task OnConnectedAsync_WithoutCharacterDbrefClaim_DoesNotAddToGroup()
	{
		var (hub, groups) = BuildHub(characterDbref: null);

		await hub.OnConnectedAsync();

		await groups.DidNotReceive().AddToGroupAsync(
			Arg.Any<string>(),
			Arg.Any<string>(),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task OnDisconnectedAsync_Always_CompletesWithoutError()
	{
		var (hub, _) = BuildHub();

		await hub.OnDisconnectedAsync(null);
		await hub.OnDisconnectedAsync(new InvalidOperationException("test"));
	}

	[Test]
	public async Task SendCommand_WithCommand_PublishesMessageForCorrectCharacter()
	{
		var (hub, _, bus) = BuildHubWithBus("77");

		await hub.SendCommand("look");

		await bus.Received(1).Publish(
			Arg.Is<GameCommandMessage>(m => m.CharacterDbref == "77" && m.Command == "look"),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task SendCommand_WithEmptyCommand_StillPublishes()
	{
		var (hub, _, bus) = BuildHubWithBus("1");

		await hub.SendCommand(string.Empty);

		await bus.Received(1).Publish(
			Arg.Is<GameCommandMessage>(m => m.CharacterDbref == "1" && m.Command == string.Empty),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task JoinRoom_WithRoomDbref_AddsToRoomGroup()
	{
		var (hub, groups) = BuildHub();

		await hub.JoinRoom("5");

		await groups.Received(1).AddToGroupAsync(
			"conn-001",
			"room:5",
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task LeaveRoom_WithRoomDbref_RemovesFromRoomGroup()
	{
		var (hub, groups) = BuildHub();

		await hub.LeaveRoom("5");

		await groups.Received(1).RemoveFromGroupAsync(
			"conn-001",
			"room:5",
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task CharacterGroupName_ReturnsExpectedFormat()
	{
		await Assert.That(GameHub.CharacterGroupName("42")).IsEqualTo("char:42");
	}

	[Test]
	public async Task RoomGroupName_ReturnsExpectedFormat()
	{
		await Assert.That(GameHub.RoomGroupName("7")).IsEqualTo("room:7");
	}
}
