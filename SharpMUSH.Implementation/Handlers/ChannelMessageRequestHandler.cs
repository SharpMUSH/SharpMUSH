using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

public partial class ChannelMessageRequestHandler(
	IPermissionService permissionService,
	INotifyService notifyService,
	ILogger<ChannelMessageRequestHandler> logger)
	: INotificationHandler<ChannelMessageNotification>
{
	public async ValueTask Handle(ChannelMessageNotification notification, CancellationToken cancellationToken)
	{
		var channelMembers = await notification.Channel.Members.Value.ToArrayAsync(cancellationToken);
		var chanName = notification.Channel.Name;
		var message = MModule.multiple([MModule.single("<"), chanName, MModule.single("> "), notification.Message]);

		// var mogrifier = notification.Channel.Mogrifier;
		// TODO: Do Mogrification stuff here.

		using (logger.BeginScope(new Dictionary<string, string>
		       {
			       ["ChannelId"] = notification.Channel.Id ?? string.Empty,
			       ["MessageType"] = notification.MessageType.ToString(),
			       ["Category"] = "logs"
		       }))
		{
			foreach (var (member, status) in channelMembers)
			{
				var isGagged = status.Gagged ?? false;
				var wantsToHear = notification.Source.IsNone ||
				                  await permissionService.CanInteract(member, notification.Source.Known(),
					                  IPermissionService.InteractType.Hear);

				if (!isGagged && wantsToHear)
				{
					await notifyService.Notify(member, message);
				}
			}

			LogChannelMessage(logger, MModule.serialize(message));
		}
	}

	[LoggerMessage(LogLevel.Information, "{ChannelMessage}")]
	static partial void LogChannelMessage(ILogger<ChannelMessageRequestHandler> logger, string channelMessage);
}