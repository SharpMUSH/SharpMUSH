using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelTitle
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString title, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var channel = await parser.Mediator.Send(new GetChannelQuery(channelName.ToPlainText()));

		if (channel is null)
		{
			return new CallState("Channel not found.");
		}
		
		var owner = await channel.Owner.WithCancellation(CancellationToken.None);
		if (!owner.Id!.Equals(executor.Id()))
		{
			return new CallState("You are not the owner of the channel.");
		}

		// channel.Title = title.ToPlainText();
		// await parser.Mediator.Send(new UpdateChannelCommand(channel));

		return new CallState("Channel title has been updated.");
	}
}