using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelWhat
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService locateService, IPermissionService permissionService, IMediator mediator, INotifyService notifyService, MString channelName)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		if (await executor.IsGuest())
		{
			await notifyService.Notify(executor, ErrorMessages.Notifications.ChatGuestsCantModify);
			return new CallState(ErrorMessages.Returns.GuestsCannotModifyChannels);
		}

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, locateService, permissionService, mediator, notifyService, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var members = channel.Members.Value;
		var memberList = members.Select(x => x.Member).ToArrayAsync();
		var memberString = string.Join(", ", memberList);

		return new CallState(MModule.single(memberString));
	}
}