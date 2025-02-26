using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelHide
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString playerName, string[] switches)
	{
		// var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var players = await parser.Mediator.Send(new GetPlayerQuery(playerName.ToPlainText()));
		var player = players.FirstOrDefault();
		
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		if (player is null)
		{
			return new CallState("Player not found.");
		}

		var members = await channel.Members.WithCancellation(CancellationToken.None);
		var (member,status) = members.FirstOrDefault(x => x.Member.Id() == player.Id);
		if (member is null)
		{
			return new CallState("Player is not a member of the channel.");
		}

		if (status.Hide ?? false)
		{
			return new CallState("Player is already hidden.");
		}

		await parser.Mediator.Send(new UpdateChannelUserStatusCommand(
			channel, member, status with { Hide = true }));

		return new CallState("Player has been hidden.");
	}
}