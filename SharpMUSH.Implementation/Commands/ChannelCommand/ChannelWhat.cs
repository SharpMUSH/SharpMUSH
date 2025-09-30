using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelWhat
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		if (await executor.IsGuest())
		{
			await NotifyService!.Notify(executor, "CHAT: Guests may not modify channels.");
			return new CallState("#-1 Guests may not modify channels.");
		}
		
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService, PermissionService, Mediator, NotifyService, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var members = await channel.Members.WithCancellation(CancellationToken.None);
		var memberList = members.Select(x => x.Member).ToList();
		var memberString = string.Join(", ", memberList);

		return new CallState(MModule.single(memberString));
	}
}