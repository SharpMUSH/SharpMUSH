using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelWho
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, string[] switches)
	{
		var channel = await parser.Mediator.Send(new GetChannelQuery(channelName.ToPlainText()));
		if (channel is null)
		{
			return new CallState("#-1 No such channel.");
		}

		var members = await channel.Members.WithCancellation(CancellationToken.None);
		var memberList = members.Select(x => x.Member).ToList();
		return new CallState(string.Join(", ", memberList));
	}
}