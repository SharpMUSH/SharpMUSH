using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelGag
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString arg1, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var caller = (await parser.CurrentState.CallerObject(parser.Mediator)).Known();
		var target = arg1.ToPlainText();
		var targetPlayers = await parser.Mediator.Send(new GetPlayerQuery(target!));
		var targetPlayer = targetPlayers.FirstOrDefault();
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		if (targetPlayer is null)
		{
			await parser.NotifyService.Notify(executor, "Player not found.", caller);
			return new CallState("Player not found.");
		}

		var members = await channel.Members.WithCancellation(CancellationToken.None);
		var (member, status) = members.FirstOrDefault(x => x.Member.Id() == targetPlayer.Id);

		if (status is null)
		{
			await parser.NotifyService.Notify(executor, "Player is not a member of the channel.", caller);
			return new CallState("Player is not a member of the channel.");
		}

		if (!switches.Contains("QUIET"))
		{
			// await parser.Mediator.Send(new GagChannelCommand(channel, targetPlayer));
			await parser.NotifyService.Notify(executor, "Player has been gagged.");
		}

		return CallState.Empty;
	}
}