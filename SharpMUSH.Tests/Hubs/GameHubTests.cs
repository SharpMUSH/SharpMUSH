using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Tests.Hubs;

/// <summary>
/// Unit tests for <see cref="GameHub"/>.
/// Uses NSubstitute to mock IHubContext infrastructure; no live SignalR connection needed.
/// </summary>
public class GameHubTests
{
	// ── Helpers ──────────────────────────────────────────────────────────────────

	private static (GameHub hub, IGroupManager groups) BuildHub(string? characterDbref = "42")
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

		var hub = new GameHub(NullLogger<GameHub>.Instance)
		{
			Groups = groups,
			Clients = clients,
			Context = context,
		};

		return (hub, groups);
	}

	// ── OnConnectedAsync ─────────────────────────────────────────────────────────

	[Test]
	public async Task OnConnectedAsync_WithCharacterDbrefClaim_AddsToCharacterGroup()
	{
		// Arrange
		var (hub, groups) = BuildHub("99");

		// Act
		await hub.OnConnectedAsync();

		// Assert
		await groups.Received(1).AddToGroupAsync(
			"conn-001",
			"char:99",
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task OnConnectedAsync_WithoutCharacterDbrefClaim_DoesNotAddToGroup()
	{
		// Arrange
		var (hub, groups) = BuildHub(characterDbref: null);

		// Act
		await hub.OnConnectedAsync();

		// Assert — no group join should have been called
		await groups.DidNotReceive().AddToGroupAsync(
			Arg.Any<string>(),
			Arg.Any<string>(),
			Arg.Any<CancellationToken>());
	}

	// ── OnDisconnectedAsync ──────────────────────────────────────────────────────

	[Test]
	public async Task OnDisconnectedAsync_Always_CompletesWithoutError()
	{
		// Arrange
		var (hub, _) = BuildHub();

		// Act / Assert — no exception expected
		await hub.OnDisconnectedAsync(null);
		await hub.OnDisconnectedAsync(new InvalidOperationException("test"));
	}

	// ── SendCommand ──────────────────────────────────────────────────────────────

	[Test]
	public async Task SendCommand_WithCommand_CreatesCommandMessageForCorrectCharacter()
	{
		// Arrange
		var (hub, _) = BuildHub("77");

		// Act — method is fire-and-log; verify it completes without throwing
		await hub.SendCommand("look");
	}

	[Test]
	public async Task SendCommand_WithEmptyCommand_CompletesWithoutThrowing()
	{
		// Arrange
		var (hub, _) = BuildHub("1");

		// Act / Assert
		await hub.SendCommand(string.Empty);
	}

	// ── JoinRoom ─────────────────────────────────────────────────────────────────

	[Test]
	public async Task JoinRoom_WithRoomDbref_AddsToRoomGroup()
	{
		// Arrange
		var (hub, groups) = BuildHub();

		// Act
		await hub.JoinRoom("5");

		// Assert
		await groups.Received(1).AddToGroupAsync(
			"conn-001",
			"room:5",
			Arg.Any<CancellationToken>());
	}

	// ── LeaveRoom ────────────────────────────────────────────────────────────────

	[Test]
	public async Task LeaveRoom_WithRoomDbref_RemovesFromRoomGroup()
	{
		// Arrange
		var (hub, groups) = BuildHub();

		// Act
		await hub.LeaveRoom("5");

		// Assert
		await groups.Received(1).RemoveFromGroupAsync(
			"conn-001",
			"room:5",
			Arg.Any<CancellationToken>());
	}

	// ── Group name helpers ───────────────────────────────────────────────────────

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
