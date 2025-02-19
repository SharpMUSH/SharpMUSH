using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelOff
{
	// TODO: Turn Off, not On
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString arg0, MString arg1, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var channelName = arg0.ToPlainText();
		var targetName = arg1.ToPlainText();
		// TODO: Use Locate
		var target = await parser.Mediator.Send(new GetPlayerQuery(targetName));
		var channel = await parser.Mediator.Send(new GetChannelQuery(channelName));
		if (channel is null)
		{
			await parser.NotifyService.Notify(executor, $"#-1 Channel {channelName} not found.");
			return new CallState("#-1 Channel not found.");
		}

		if (!target.Any())
		{
			await parser.NotifyService.Notify(executor, $"#-1 Player {targetName} not found.");
			return new CallState("#-1 Player not found.");
		}

		// TODO: Announce Channel Join
		// await parser.Mediator.Send(new JoinChannelCommand(target, channel));
		await parser.NotifyService.Notify(executor, $"#-1 {targetName} has been added to {channelName}.");
		return new CallState($"#-1 {targetName} has been added to {channelName}.");
	}
}