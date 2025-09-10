using Mediator;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

public class ChannelMessageRequestHandler(IPermissionService permissionService, INotifyService notifyService): INotificationHandler<ChannelMessageNotification>
{
	public async ValueTask Handle(ChannelMessageNotification notification, CancellationToken cancellationToken)
	{
		var channelMembers = await notification.Channel.Members.WithCancellation(cancellationToken);
		var chanName = notification.Channel.Name;
		var message = MModule.multiple([MModule.single("<"), chanName, MModule.single("> "), notification.Message]);
		
		// var mogrifier = notification.Channel.Mogrifier;
		// TODO: Do Mogrification stuff here.
		
		foreach(var (member,status) in channelMembers)
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
	}
}