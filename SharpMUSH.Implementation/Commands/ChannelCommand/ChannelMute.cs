using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelMute
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName, MString playerName)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await executor.IsGuest())
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatGuestsCantModify, executor);
			return new CallState(ErrorMessages.Returns.GuestsCannotModifyChannels);
		}

		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(parser, PermissionService, Mediator,
			NotifyService, executor, channelName, true);
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		// @channel/mute writes the status of a player the executor NAMES, unlike /gag, /hide, /combine and
		// /title, which only ever write the executor's own. A third-party write needs the channel's modify
		// right; without this any non-guest who knows a channel and a member name could mute that member.
		// Silencing someone else's channel voice is exactly what ChanModLock exists to control.
		if (!await PermissionService.ChannelCanModifyAsync(executor, channel))
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.PermissionDenied, executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var players = Mediator.CreateStream(new GetPlayerQuery(playerName.ToPlainText()));
		var player = await players.FirstOrDefaultAsync();
		if (player is null)
		{
			return new CallState("Player not found.");
		}

		var memberStatus = await ChannelHelper.ChannelMemberStatus(player, channel);
		if (memberStatus is null)
		{
			return new CallState("Player is not a member of the channel.");
		}

		var (_, status) = memberStatus;

		if (status.Mute ?? false)
		{
			return new CallState("Player is already muted.");
		}

		// The status was written against the executor, so @channel/mute muted whoever issued it rather
		// than the player they named.
		await Mediator.Send(new UpdateChannelUserStatusCommand(channel, player,
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