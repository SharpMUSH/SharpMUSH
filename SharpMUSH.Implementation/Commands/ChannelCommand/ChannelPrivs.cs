using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelPrivs
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName, MString privs)
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

		// The sense of this check was inverted: whoever COULD modify the channel was refused,
		// and whoever could not fell through and made the change.
		if (!await PermissionService.ChannelCanModifyAsync(executor, channel))
		{
			await NotifyService.Notify(executor, "You are not the owner of the channel.", executor);
			return new CallState("You are not the owner of the channel.");
		}

		// extchat.c:1831 — `type = string_to_privs(priv_table, perms, ChanType(chan))`. The list is applied
		// TO the channel's current privileges, not substituted for them, and `!priv` removes one. This used
		// to replace the whole set, so `@channel/privs Pub=quiet` silently dropped the Player bit and left
		// a channel nobody was the right type for.
		var privilegeList = ChannelHelper.StringToChannelPrivileges(privs, channel.Privs);
		if (privilegeList.IsError)
		{
			await NotifyService.Notify(executor,
				$"CHAT: Invalid channel privileges(s):  {string.Join(",", privilegeList.AsError.Value)}", executor);
			return new CallState(ErrorMessages.Returns.InvalidPrivileges);
		}

		// extchat.c:1832 — Chan_Can_Priv against the type being SET.
		if (!await PermissionService.ChannelCanPriv(executor, privilegeList.AsPrivileges))
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatCannotMakeThatType, executor);
			return new CallState(ErrorMessages.Returns.ChannelPermissionDenied);
		}

		// extchat.c:1836
		if (privilegeList.AsPrivileges.HasPriv("Disabled"))
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatChannelWillBeDisabled, executor);
		}

		await Mediator.Send(new UpdateChannelCommand(channel,
			null,
			null,
			Privs: privilegeList.AsPrivileges,
			null,
			null,
			null,
			null,
			null,
			null,
			null));

		await NotifyService.Notify(executor, "CHAT: Channel privileges have been updated.", executor);
		return new CallState("CHAT: Channel privileges have been updated.");
	}
}