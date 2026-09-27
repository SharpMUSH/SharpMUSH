using Mediator;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

/// <summary>
/// Routes a published <see cref="ChannelMessageNotification"/> to <see cref="IChannelBroadcastService"/>,
/// which owns the channel send.
/// </summary>
public class ChannelMessageRequestHandler(IChannelBroadcastService broadcastService)
	: INotificationHandler<ChannelMessageNotification>
{
	public ValueTask Handle(ChannelMessageNotification notification, CancellationToken cancellationToken)
		=> broadcastService.BroadcastAsync(notification, cancellationToken);
}
