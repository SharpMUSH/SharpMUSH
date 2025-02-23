using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelDelete
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString message, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		if (await executor.IsGuest())
		{
			await parser.NotifyService.Notify(executor, "Guests may not modify channels.");
			return new CallState("#-1 Guests may not modify channels.");
		}
		
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, channelName, true);

		if (maybeChannel.IsError)
		{
			await parser.NotifyService.Notify(executor, maybeChannel.AsError.Value.Message!);
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;


		if (!channel.Id!.Equals(executor.Id()))
		{
			return new CallState("You are not the owner of the channel.");
		}

		await parser.Mediator.Send(new DeleteChannelCommand(channel));

		return new CallState("Channel has been deleted.");
	}
}