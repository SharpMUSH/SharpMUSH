using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelWhat
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString arg0, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var caller = (await parser.CurrentState.CallerObject(parser.Mediator)).Known();
		var channelName = arg0.ToPlainText();
		var channel = await parser.Mediator.Send(new GetChannelQuery(channelName));

		if (channel is null)
		{
			await parser.NotifyService.Notify(executor, $"#-1 Channel '{channelName}' not found.", caller);
			return new CallState(MModule.single("#-1 Channel not found."));
		}

		var members = await channel.Members.WithCancellation(CancellationToken.None);
		var memberList = members.Select(x => x.Member).ToList();
		var memberString = string.Join(", ", memberList);

		return new CallState(MModule.single(memberString));
	}
}