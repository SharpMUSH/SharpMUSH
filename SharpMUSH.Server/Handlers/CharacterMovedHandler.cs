using Mediator;
using Microsoft.AspNetCore.SignalR;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Server.Handlers;

/// <summary>
/// Tells a player's portal tabs where the player moved to, so the World map can follow them. Only the player's own
/// tabs hear it: the character group holds the connections playing that character.
/// </summary>
public class CharacterMovedHandler(IHubContext<GameHub, IGameHubClient> hub) : INotificationHandler<ObjectMovedNotification>
{
	public async ValueTask Handle(ObjectMovedNotification notification, CancellationToken cancellationToken)
	{
		if (notification.Target is not SharpPlayer player) return;
		await hub.Clients.Group(GameHub.CharacterGroupName(player.Object.DBRef))
			.ReceiveCharacterMoved(new CharacterMovedMessage(notification.NewLocation.Object().DBRef.ToString()));
	}
}
