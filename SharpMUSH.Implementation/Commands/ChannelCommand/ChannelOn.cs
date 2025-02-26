using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelOn
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString arg1, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var targetName = arg1.ToPlainText();

		var maybeTarget = await parser.LocateService.Locate(parser, executor, executor, targetName, LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference | LocateFlags.MatchOptionalWildCardForPlayerName);

		if (maybeTarget.IsError)
		{
			await parser.NotifyService.Notify(executor, maybeTarget.AsError.Value);
			return new CallState(maybeTarget.AsError.Value);
		}

		var target = maybeTarget.AsAnyObject;
		
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		// TODO: Announce Channel Join
		await parser.Mediator.Send(new AddUserToChannelCommand(channel, target));

		await parser.NotifyService.Notify(executor, $"CHAT: {targetName} has been added to {channelName}.");
		return new CallState($"{targetName} has been added to {channelName}.");
	}
}