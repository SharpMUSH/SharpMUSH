using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelPrivs
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString privs, string[] switches)
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
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var owner = await channel.Owner.WithCancellation(CancellationToken.None);
		if (!owner.Id!.Equals(executor.Id()))
		{
			return new CallState("You are not the owner of the channel.");
		}

		// channel.Privs = privs.ToPlainText();
		// await parser.Mediator.Send(new UpdateChannelCommand(channel));

		return new CallState("Channel privs have been updated.");
	}
}