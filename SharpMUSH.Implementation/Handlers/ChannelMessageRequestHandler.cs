using Mediator;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Handlers;

public class ChannelMessageRequestHandler(IMUSHCodeParser parser): INotificationHandler<ChannelMessageRequest>
{
	public async ValueTask Handle(ChannelMessageRequest notification, CancellationToken cancellationToken)
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
			                  await parser.PermissionService.CanInteract(member, notification.Source.Known(),
				                  IPermissionService.InteractType.Hear);

			if (!isGagged && wantsToHear)
			{
				await parser.NotifyService.Notify(member, message);
			}
		}
	}
}