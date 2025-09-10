using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelWipe
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName, MString message)
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

		if (await PermissionService!.ChannelCanModifyAsync(executor, channel))
		{
			return new CallState("You are not the owner of the channel.");
		}

		channel.Buffer = 0;
		// await Mediator!.Send(new UpdateChannelCommand(channel));

		return new CallState("Channel buffer has been wiped.");
	}
}