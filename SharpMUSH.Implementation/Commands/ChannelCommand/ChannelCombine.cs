using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelCombine
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, MString channelName, MString playerName)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		if (await executor.IsGuest())
		{
			await parser.NotifyService.Notify(executor, "CHAT: Guests may not modify channels.");
			return new CallState("#-1 Guests may not modify channels.");
		}
		
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var locate =
			await parser.LocateService.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, playerName.ToPlainText());

		switch (locate)
		{
			case { IsError: true }:
				return new CallState(locate.AsError.Value);
			case { IsNone: true }:
				return new CallState("#-1 PLAYER NOT FOUND");
		}

		var player = locate.AsPlayer;
		var members = await channel.Members.WithCancellation(CancellationToken.None);
		var (member,memberStatus) = members.FirstOrDefault(x => x.Member.Object().Id == player.Object.Id);
		if (member is null)
		{
			return new CallState("#-1 Player is not a member of the channel.");
		}

		if (memberStatus.Combine ?? true)
		{
			return new CallState("#-1 Player is already combined.");
		}

		await parser.Mediator.Send(new UpdateChannelUserStatusCommand(channel, executor, new SharpChannelStatus(
				Combine: true,
				null,
				null,
				null,
				null
			)));

		return new CallState("Player has been combined.");
	}
}