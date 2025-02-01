using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelChown
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString newOwner, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var channel = await parser.Mediator.Send(new GetChannelQuery(channelName.ToPlainText()));

		if (channel is null)
		{
			return new CallState("Channel not found.");
		}

		// TODO: Use Locate()
		var newOwnerObject = await parser.Mediator.Send(new GetPlayerQuery(newOwner.ToPlainText()));
		if (newOwnerObject is null)
		{
			return new CallState("New owner not found.");
		}
		
		// TODO: This needs a new command.
		// await parser.Mediator.Send(new UpdateChannelCommand(channel));

		return new CallState("Channel owner has been updated.");
	}
}