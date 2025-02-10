using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelDecompile
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString prefix, MString brief, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var channels = await parser.Mediator.Send(new GetChannelQuery(prefix.ToPlainText()));

		if (channels is null)
		{
			return new CallState("No channels found.");
		}
		
		// TODO: Implement the rest of the command.
		await parser.NotifyService.Notify(executor, string.Empty, executor);

		return new CallState(string.Empty);
	}
}