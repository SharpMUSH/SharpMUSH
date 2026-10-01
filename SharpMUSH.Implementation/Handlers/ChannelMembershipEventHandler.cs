using Mediator;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

/// <summary>
/// Raises <see cref="SharpEvents.PlayerChannels"/> when a connected player's channel list changes, so a
/// handler can push the new list to that player's client (the <c>comm-feed</c> package's
/// <c>comm.channels</c>).
/// </summary>
/// <remarks>
/// Only for a connected player: the event exists to refresh a client, and a player who is not
/// connected is sent their whole list when they connect. That also keeps a database import, which joins
/// every member of every channel, from running softcode once per membership.
/// </remarks>
public class ChannelMembershipEventHandler(
	IEventService eventService,
	IConnectionService connectionService,
	IMUSHCodeParser parser)
	: INotificationHandler<ChannelMembershipChangedNotification>
{
	public async ValueTask Handle(ChannelMembershipChangedNotification notification, CancellationToken cancellationToken)
	{
		if (!notification.Member.IsPlayer || !await connectionService.IsConnected(notification.Member))
		{
			return;
		}

		await eventService.TriggerEventAsync(
			parser,
			SharpEvents.PlayerChannels,
			null,
			notification.Member.Object().DBRef.ToString(),
			notification.Cause,
			notification.ChannelName);
	}
}
