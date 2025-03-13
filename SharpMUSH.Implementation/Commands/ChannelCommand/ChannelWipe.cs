using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelWipe
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString message)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		if (await executor.IsGuest())
		{
			await parser.NotifyService.Notify(executor, "CHAT: Guests may not modify channels.");
			return new CallState("#-1 Guests may not modify channels.");
		}

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		if (await parser.PermissionService.ChannelCanModifyAsync(executor, channel))
		{
			return new CallState("You are not the owner of the channel.");
		}

		channel.Buffer = 0;
		// await parser.Mediator.Send(new UpdateChannelCommand(channel));

		return new CallState("Channel buffer has been wiped.");
	}
}