using SharpMUSH.Library;
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
		if (await executor.IsGuest())
		{
			await parser.NotifyService.Notify(executor, "Guests may not modify channels.");
			return new CallState("#-1 Guests may not modify channels.");
		}
		
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, channelName, true);

		if (maybeChannel.IsError)
		{
			await parser.NotifyService.Notify(executor, maybeChannel.AsError.Value.Message!);
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;
		
		// TODO: PERMISSION CHECK

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