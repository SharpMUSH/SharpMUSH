using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelList
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString arg0, MString arg1, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var caller = (await parser.CurrentState.CallerObject(parser.Mediator)).Known();
		var channels = await parser.Mediator.Send(new GetChannelListQuery());
		var channelList = channels.Select(channel => channel.Name);
		return new CallState(MModule.multiple(channelList));
	}
}