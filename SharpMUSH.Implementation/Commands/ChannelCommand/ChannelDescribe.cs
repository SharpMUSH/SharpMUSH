using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelDescribe
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString description)
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
			return new CallState("You cannot modify this channel.");
		}

		await parser.Mediator.Send(new UpdateChannelCommand(channel,
			null, 
			description,
			null,
			null,
			null,
          			null,
			null,
			null,
			null, 
			null));

		return new CallState("Channel description has been updated.");
	}
}