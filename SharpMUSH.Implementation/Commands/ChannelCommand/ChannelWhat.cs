using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelWhat
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService locateService, IPermissionService permissionService, IMediator mediator, INotifyService notifyService, MString channelName)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		if (await executor.IsGuest())
		{
			await notifyService.Notify(executor, "CHAT: Guests may not modify channels.");
			return new CallState("#-1 Guests may not modify channels.");
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