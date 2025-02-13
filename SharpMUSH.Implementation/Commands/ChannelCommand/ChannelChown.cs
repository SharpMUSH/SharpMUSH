using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
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
		
		// TODO: PERMISSION CHECK

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

		await parser.Mediator.Send(new UpdateChannelOwnerCommand(channel, newOwnerObject));

		var output = MModule.multiple([MModule.single("CHAT: "), MModule.single(executor.Object().Name), MModule.single(" is the new owner of "), channel.Name]);
		await parser.NotifyService.Notify(executor, output);
		return new CallState(string.Empty);
	}
}