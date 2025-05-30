using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelBuffer
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString lines)
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
			null, 
			Buffer: linesInt));

		return new CallState("Channel buffer has been updated.");
	}
}