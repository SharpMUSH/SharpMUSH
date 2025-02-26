using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelMute
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString playerName, string[] switches)
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
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var players = await parser.Mediator.Send(new GetPlayerQuery(playerName.ToPlainText()));
		var player = players.FirstOrDefault();
		if (player is null)
		{
			return new CallState("Player not found.");
		}

		var members = await channel.Members.WithCancellation(CancellationToken.None);
		var (member,status) = members.FirstOrDefault(x => x.Member.Id() == player.Id!);
		if (member is null)
		{
			return new CallState("Player is not a member of the channel.");
		}

		if (status.Mute ?? false)
		{
			return new CallState("Player is already muted.");
		}

		// status.Mute = true;
		// await parser.Mediator.Send(new UpdateChannelMemberCommand(member));

		return new CallState("Player has been muted.");
	}
}