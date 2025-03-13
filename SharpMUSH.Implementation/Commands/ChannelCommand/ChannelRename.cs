using SharpMUSH.Database.Models;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelRename
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString newChannelName)
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

		var isValid = ChannelHelper.IsValidChannelName(parser, newChannelName);
		if (!isValid)
		{
			await parser.NotifyService.Notify(executor, "CHAT: Invalid channel name.");
			return new CallState("#-1 CHAT: Invalid channel name.");
		}

		await parser.Mediator.Send(new UpdateChannelCommand(channel,
			newChannelName,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null
		));

		await parser.NotifyService.Notify(executor, "CHAT: Renamed channel.");
		return new CallState("Renamed channel.");
	}
}