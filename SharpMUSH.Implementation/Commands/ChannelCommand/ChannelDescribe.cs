using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelDescribe
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString description, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var channel = await parser.Mediator.Send(new GetChannelQuery(channelName.ToPlainText()));

		if (channel is null)
		{
			return new CallState("Channel not found.");
		}

		if (!channel.Id!.Equals(executor.Id()))
		{
			return new CallState("You are not the owner of the channel.");
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
			null));

		return new CallState("Channel description has been updated.");
	}
}