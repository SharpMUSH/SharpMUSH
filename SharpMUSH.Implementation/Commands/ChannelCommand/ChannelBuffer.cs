using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelBuffer
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString lines)
	{
		// TODO: How the heck are we going to handle Channel Buffers?
		// Channel Buffer can likely sit in a temporary file. Only lost on shutdown, not reboot?
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
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

		if (!int.TryParse(lines.ToPlainText(), out var linesInt))
		{
			return new CallState("Invalid number of lines.");
		}

		await parser.Mediator.Send(new UpdateChannelCommand(
			Channel: channel,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			Buffer: linesInt));

		return new CallState("Channel buffer has been updated.");
	}
}