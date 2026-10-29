using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Server.Handlers;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Tests.Services;

namespace SharpMUSH.Tests.Hubs;

/// <summary>A player's portal tabs hear where the player moved to; nobody hears a thing being moved.</summary>
public class CharacterMovedHandlerTests
{
	private readonly TestObjectFactory _objects = new();

	private static (CharacterMovedHandler Handler, IHubClients<IGameHubClient> Clients, IGameHubClient Group) Build()
	{
		var group = Substitute.For<IGameHubClient>();
		var clients = Substitute.For<IHubClients<IGameHubClient>>();
		clients.Group(Arg.Any<string>()).Returns(group);
		var hub = Substitute.For<IHubContext<GameHub, IGameHubClient>>();
		hub.Clients.Returns(clients);
		return (new CharacterMovedHandler(hub), clients, group);
	}

	private static ObjectMovedNotification Moved(AnySharpContent target, SharpRoom to) =>
		new(target, new AnySharpContainer(to), new DBRef(1), null, false, "move");

	[Test]
	public async Task APlayerWhoMovesTellsTheirOwnTabsWhere()
	{
		var (handler, clients, group) = Build();
		var player = _objects.CreatePlayer(42, "Walker").Expect<SharpPlayer>();
		var tower = _objects.CreateRoom(31, "Watchtower");

		await handler.Handle(Moved(new AnySharpContent(player), tower), CancellationToken.None);

		clients.Received(1).Group(GameHub.CharacterGroupName(player.Object.DBRef));
		await group.Received(1).ReceiveCharacterMoved(new CharacterMovedMessage(tower.Object.DBRef.ToString()));
	}

	[Test]
	public async Task AThingThatIsMovedTellsNobody()
	{
		var (handler, clients, _) = Build();
		var crate = _objects.CreateThing(43, "Crate").Expect<SharpThing>();

		await handler.Handle(Moved(new AnySharpContent(crate), _objects.CreateRoom(31, "Watchtower")), CancellationToken.None);

		clients.DidNotReceive().Group(Arg.Any<string>());
	}
}
