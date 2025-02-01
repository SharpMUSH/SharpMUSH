using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelMogrifier
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString objectName, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var channel = await parser.Mediator.Send(new GetChannelQuery(channelName.ToPlainText()));

		if (channel is null)
		{
			return new CallState("Channel not found.");
		}

		// TODO: Locate Object
		var locate = new { };
		var objectNameString = objectName.ToPlainText();
		// var obj = await parser.Mediator.Send(new GetObjectQuery(objectNameString));

		if (locate is null)
		{
			return new CallState("Object not found.");
		}

		// channel.Mogrifier = object.Id;
		// await parser.Mediator.Send(new UpdateChannelCommand(channel));

		return new CallState("Channel mogrifier has been updated.");
	}
}