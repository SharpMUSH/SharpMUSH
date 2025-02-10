using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelCombine
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString playerName, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var channel = await parser.Mediator.Send(new GetChannelQuery(channelName.ToPlainText()));
		var player = await parser.Mediator.Send(new GetPlayerQuery(playerName.ToPlainText()));

		if (channel is null)
		{
			return new CallState("#-1 Channel not found.");
		}

		// TODO: Use Locate
		if (player is null)
		{
			return new CallState("#-1 Player not found.");
		}

		var members = await channel.Members.WithCancellation(CancellationToken.None);
		var (member,memberStatus) = members.FirstOrDefault(x => x.Member.Id() == player.First().Id);
		if (member is null)
		{
			return new CallState("#-1 Player is not a member of the channel.");
		}

		if (memberStatus.Combine ?? true)
		{
			return new CallState("#-1 Player is already combined.");
		}

		// TODO: Don't use SharpChannelStatus here.
		// await parser.Mediator.Send(new UpdateChannelUserStatusCommand(channel, player, new SharpChannelStatus(), ));

		return new CallState("Player has been combined.");
	}
}