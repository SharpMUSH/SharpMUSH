using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelMute
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName, MString playerName)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		if (await executor.IsGuest())
		{
			await NotifyService!.Notify(executor, "CHAT: Guests may not modify channels.");
			return new CallState("#-1 Guests may not modify channels.");
		}

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService, PermissionService, Mediator, NotifyService, channelName, true);
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var players = await Mediator!.Send(new GetPlayerQuery(playerName.ToPlainText()));
		var player = players.FirstOrDefault();
		if (player is null)
		{
			return new CallState("Player not found.");
		}

		var memberStatus = await ChannelHelper.ChannelMemberStatus(player, channel);
		if (memberStatus is null)
		{
			return new CallState("Player is not a member of the channel.");
		}

		var (_, status) = memberStatus.Value;

		if (status.Mute ?? false)
		{
			return new CallState("Player is already muted.");
		}

		await Mediator!.Send(new UpdateChannelUserStatusCommand(channel, executor,
			new SharpChannelStatus(
				null,
				null,
				null,
				true,
				null
			)));

		return new CallState("Player has been muted.");
	}
}