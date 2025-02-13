using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelChown
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString newOwner,
		string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var locate = await parser.LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, newOwner.ToPlainText(),
			LocateFlags.PlayersPreference
			| LocateFlags.OnlyMatchTypePreference
			| LocateFlags.MatchOptionalWildCardForPlayerName);

		switch (locate)
		{
			case { IsError: true }:
				return new CallState(locate.AsError.Value);
			case { IsNone: true }:
				return new CallState("#-1 PLAYER NOT FOUND");
		}

		var newOwnerObject = locate.AsPlayer;

		// TODO: This needs a new command.
		// await parser.Mediator.Send(new UpdateChannelCommand(channel));

		return new CallState("Channel owner has been updated.");
	}
}