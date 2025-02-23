using SharpMUSH.Library;
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