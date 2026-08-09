using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelHide
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString? channelName, MString? yesNo)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		ImmutableArray<SharpChannel> channels;

		if (await executor.IsGuest())
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatGuestsCantModify, executor);
			return new CallState(ErrorMessages.Returns.GuestsCannotModifyChannels);
		}

		var yesNoString = yesNo?.ToPlainText();
		if (yesNoString is not null && !(yesNoString.Equals("yes", StringComparison.InvariantCultureIgnoreCase) ||
																		 yesNoString.Equals("no", StringComparison.InvariantCultureIgnoreCase)))
		{
			await NotifyService.Notify(executor, "CHAT: Yes or No are the only valid options.", executor);
			return new CallState(ErrorMessages.Returns.InvalidOption);
		}

		// The sense of this was inverted: naming a channel hid you on EVERY channel, and naming none
		// passed a null name to a lookup that cannot take one.
		if (channelName is null)
		{
			// The bulk path routed around GetVisibleChannelOrError entirely and then named each channel in
			// its per-channel notifications, so `@channel/hide` with no argument listed exactly the channels
			// that gate exists to hide.
			channels = [.. await ChannelHelper.VisibleChannels(PermissionService, executor,
				Mediator.CreateStream(new GetChannelListQuery()))];
		}
		else
		{
			var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(parser, PermissionService, Mediator,
				NotifyService, executor, channelName, true);
			if (maybeChannel.IsError)
			{
				return maybeChannel.AsError.Value;
			}

			channels = [maybeChannel.AsChannel];
		}

		var hideOn = yesNoString?.Equals("yes", StringComparison.OrdinalIgnoreCase) ?? true;

		foreach (var channel in channels)
		{
			var maybeMemberStatus = await ChannelHelper.ChannelMemberStatus(executor, channel);

			if (maybeMemberStatus is null)
			{
				await NotifyService.Notify(executor, $"CHAT: You are not a member of {channel.Name.ToPlainText()}.", executor);
				continue;
			}

			// extchat.c:2001 — `if (!Chan_Can_Hide(c, player) && !Wizard(player))`. The Hide_Ok privilege and
			// the hide lock decide who may vanish from a channel's who-list; nothing consulted them before.
			if (!await PermissionService.ChannelCanHide(executor, channel) && !await executor.IsWizard())
			{
				await NotifyService.Notify(executor,
					string.Format(ErrorMessages.Notifications.ChatCannotHideOnChannel, channel.Name.ToPlainText()),
					executor);
				continue;
			}

			// `status?.Hide ?? false == hideOn` binds as `status?.Hide ?? (false == hideOn)`, which reports
			// "already in that hide state" whenever the player was NOT hidden and asked to unhide — and
			// never reported it when they were.
			if ((maybeMemberStatus.Status.Hide ?? false) == hideOn)
			{
				await NotifyService.Notify(executor, $"CHAT: You are already in that hide state on {channel.Name.ToPlainText()}.", executor);
				continue;
			}

			await Mediator.Send(new UpdateChannelUserStatusCommand(
				channel, executor, new SharpChannelStatus(
					null,
					null,
					hideOn,
					null,
					null
				)));

			if (hideOn)
			{
				await NotifyService.Notify(executor, $"CHAT: You have been hidden on {channel.Name.ToPlainText()}.", executor);
			}
			else
			{
				await NotifyService.Notify(executor, $"CHAT: You have been unhidden on {channel.Name.ToPlainText()}.", executor);
			}
		}

		return new CallState(channels.Length);
	}
}