using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Tests.Hubs;

/// <summary>
/// Unit tests for <see cref="GameHub"/>.
/// Uses NSubstitute to mock IHubContext infrastructure; no live SignalR connection needed.
/// </summary>
public class GameHubTests
{
	/// <summary>
	/// A partial-mocked <see cref="SitelockGuard"/> (its <c>IsBlocked</c> is <c>virtual</c> for exactly
	/// this reason) that answers a fixed verdict without needing a full <see cref="SharpMUSHOptions"/>.
	/// </summary>
	private static SitelockGuard BuildSitelockGuard(bool blocked)
	{
		var guard = Substitute.For<SitelockGuard>(Substitute.For<IOptionsWrapper<SharpMUSHOptions>>());
		guard.IsBlocked(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(blocked);
		return guard;
	}

	private static (GameHub hub, IGroupManager groups) BuildHub(string? characterDbref = "#42:1700000000")
	{
		var (hub, groups, _) = BuildHubWithBus(characterDbref);
		return (hub, groups);
	}

	private static (GameHub hub, IGroupManager groups, IMessageBus bus) BuildHubWithBus(
		string? characterDbref = "#42:1700000000", SitelockGuard? sitelockGuard = null)
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

		var registry = new HubConnectionRegistry();
		var guard = sitelockGuard ?? BuildSitelockGuard(blocked: false);

		var hub = new GameHub(bus, NullLogger<GameHub>.Instance, registry, guard)
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
		var (hub, groups) = BuildHub("#99:1700000000");

		await hub.OnConnectedAsync();

		await groups.Received(1).AddToGroupAsync(
			"conn-001",
			"char:#99:1700000000",
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
	public async Task OnConnectedAsync_WhenSitelockBlocked_AbortsAndNeverJoinsGroup()
	{
		var (hub, groups, _) = BuildHubWithBus("#42:1700000000", sitelockGuard: BuildSitelockGuard(blocked: true));

		await hub.OnConnectedAsync();

		hub.Context.Received(1).Abort();
		await groups.DidNotReceive().AddToGroupAsync(
			Arg.Any<string>(),
			Arg.Any<string>(),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task OnConnectedAsync_WhenSitelockNotBlocked_JoinsGroupNormally()
	{
		var (hub, groups, _) = BuildHubWithBus("#42:1700000000", sitelockGuard: BuildSitelockGuard(blocked: false));

		await hub.OnConnectedAsync();

		hub.Context.DidNotReceive().Abort();
		await groups.Received(1).AddToGroupAsync("conn-001", "char:#42:1700000000", Arg.Any<CancellationToken>());
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
		var (hub, _, bus) = BuildHubWithBus("#77:1700000000");

		await hub.SendCommand("look");

		await bus.Received(1).Publish(
			Arg.Is<GameCommandMessage>(m => m.CharacterDbref == "#77:1700000000" && m.Command == "look"),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task SendCommand_WithEmptyCommand_StillPublishes()
	{
		var (hub, _, bus) = BuildHubWithBus("#1:1700000000");

		await hub.SendCommand(string.Empty);

		await bus.Received(1).Publish(
			Arg.Is<GameCommandMessage>(m => m.CharacterDbref == "#1:1700000000" && m.Command == string.Empty),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task JoinRoom_WithRoomDbref_AddsToRoomGroup()
	{
		var (hub, groups) = BuildHub();

		await hub.JoinRoom("#5:1700000000");

		await groups.Received(1).AddToGroupAsync(
			"conn-001",
			"room:#5:1700000000",
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task LeaveRoom_WithRoomDbref_RemovesFromRoomGroup()
	{
		var (hub, groups) = BuildHub();

		await hub.LeaveRoom("#5:1700000000");

		await groups.Received(1).RemoveFromGroupAsync(
			"conn-001",
			"room:#5:1700000000",
			Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// A room reference that cannot be routed is rejected at the boundary — unparseable
	/// (<c>"not-a-dbref"</c>, <c>"5"</c>, empty, null) or parseable but incomplete (<c>"#5"</c>,
	/// which names <c>room:#5</c> while publishers name <c>room:#5:creation</c>). Previously any of
	/// them was interpolated straight into a group name, so the caller silently joined a group
	/// nothing ever publishes to.
	/// </summary>
	[Test]
	[Arguments("5")]
	[Arguments("not-a-dbref")]
	[Arguments("")]
	[Arguments(null)]
	[Arguments("#5")]
	public async Task JoinRoom_WithUnroutableReference_Throws(string? roomDbref)
	{
		var (hub, groups) = BuildHub();

		await Assert.That(async () => await hub.JoinRoom(roomDbref)).Throws<HubException>();

		await groups.DidNotReceive().AddToGroupAsync(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// A claim carrying a bare number rather than a dbref or objid does not resolve, so the
	/// connection joins no character group. Every handler emits the objid form.
	/// </summary>
	/// <summary>
	/// A bare dbref is refused rather than routed. It parses, so it would have joined
	/// <c>char:#42</c> while publishers name <c>char:#42:creation</c> — the same silent-drop the
	/// objid contract exists to prevent, reintroduced by an incomplete reference rather than a
	/// misspelled one.
	/// </summary>
	[Test]
	public async Task OnConnectedAsync_WithBareDbrefClaim_DoesNotAddToGroup()
	{
		var (hub, groups) = BuildHub("#42");

		await hub.OnConnectedAsync();

		await groups.DidNotReceive().AddToGroupAsync(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task SendCommand_WithBareDbrefClaim_Throws()
	{
		var (hub, _, bus) = BuildHubWithBus("#42");

		await Assert.That(async () => await hub.SendCommand("look")).Throws<HubException>();

		await bus.DidNotReceive().Publish(
			Arg.Any<GameCommandMessage>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task OnConnectedAsync_WithUnparseableCharacterClaim_DoesNotAddToGroup()
	{
		var (hub, groups) = BuildHub("42");

		await hub.OnConnectedAsync();

		await groups.DidNotReceive().AddToGroupAsync(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task SendCommand_WithUnparseableCharacterClaim_Throws()
	{
		var (hub, _, bus) = BuildHubWithBus("42");

		await Assert.That(async () => await hub.SendCommand("look")).Throws<HubException>();

		await bus.DidNotReceive().Publish(
			Arg.Any<GameCommandMessage>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task CharacterGroupName_UsesTheObjid()
	{
		await Assert.That(GameHub.CharacterGroupName(new DBRef(42, 1700000000)))
			.IsEqualTo("char:#42:1700000000");
	}

	[Test]
	public async Task RoomGroupName_UsesTheObjid()
	{
		await Assert.That(GameHub.RoomGroupName(new DBRef(7, 1700000000)))
			.IsEqualTo("room:#7:1700000000");
	}

	/// <summary>
	/// The contract that makes subscriber and publisher agree: whatever a producer writes with
	/// <see cref="DBRef.ToString"/>, a consumer parses with <see cref="DBRef.TryParse"/> back to the
	/// same group name. Publisher and subscriber reach the group by different routes — a claim, a
	/// client argument, a NATS payload — and this is what stops them diverging.
	/// </summary>
	[Test]
	[Arguments(42, 1700000000L)]
	[Arguments(42, null)]
	[Arguments(1, 0L)]
	public async Task GroupName_RoundTripsThroughTheWireForm(int number, long? creationMilliseconds)
	{
		var produced = new DBRef(number, creationMilliseconds);

		var onTheWire = produced.ToString();
		await Assert.That(DBRef.TryParse(onTheWire, out var consumed)).IsTrue();
		var roundTripped = consumed!.Value;

		await Assert.That(GameHub.CharacterGroupName(roundTripped))
			.IsEqualTo(GameHub.CharacterGroupName(produced));
		await Assert.That(GameHub.RoomGroupName(roundTripped))
			.IsEqualTo(GameHub.RoomGroupName(produced));
	}

	/// <summary>
	/// A bare dbref and the same object's objid are deliberately *different* groups: the objid is
	/// what makes delivery recycle-safe, so collapsing them would resurrect the bug this contract
	/// exists to prevent.
	/// </summary>
	[Test]
	public async Task GroupName_ObjidAndBareDbrefAreDistinctGroups()
	{
		await Assert.That(GameHub.CharacterGroupName(new DBRef(42, 1700000000)))
			.IsNotEqualTo(GameHub.CharacterGroupName(new DBRef(42)));
	}
}
