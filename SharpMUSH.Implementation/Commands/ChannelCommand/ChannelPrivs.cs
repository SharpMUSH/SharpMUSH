using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

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

		return await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, true) switch
		{
			SharpChannel channel => await SetPrivilegesAsync(PermissionService, Mediator, NotifyService, executor, channel,
				privs),
			Error<CallState> error => error.Value
		};
	}

	private static async ValueTask<CallState> SetPrivilegesAsync(IPermissionService PermissionService,
		IMediator Mediator, INotifyService NotifyService, AnySharpObject executor, SharpChannel channel, MString privs)
	{
		if (!await PermissionService.ChannelCanModifyAsync(executor, channel))
		{
			await NotifyService.Notify(executor, "You are not the owner of the channel.", executor);
			return new CallState("You are not the owner of the channel.");
		}

		// extchat.c:1831 — `type = string_to_privs(priv_table, perms, ChanType(chan))`. The list is applied
		// TO the channel's current privileges, not substituted for them, and `!priv` removes one. This used
		// to replace the whole set, so `@channel/privs Pub=quiet` silently dropped the Player bit and left
		// a channel nobody was the right type for.
		return ChannelHelper.StringToChannelPrivileges(privs, channel.Privs) switch
		{
			string[] privileges => await ApplyPrivilegesAsync(PermissionService, Mediator, NotifyService, executor,
				channel, privileges),
			Error<string[]> invalid => await RefuseInvalidPrivilegesAsync(NotifyService, executor, invalid.Value)
		};
	}

	private static async ValueTask<CallState> RefuseInvalidPrivilegesAsync(INotifyService NotifyService,
		AnySharpObject executor, string[] invalid)
	{
		await NotifyService.Notify(executor,
			$"CHAT: Invalid channel privileges(s):  {string.Join(",", invalid)}", executor);
		return new CallState(ErrorMessages.Returns.InvalidPrivileges);
	}

	private static async ValueTask<CallState> ApplyPrivilegesAsync(IPermissionService PermissionService,
		IMediator Mediator, INotifyService NotifyService, AnySharpObject executor, SharpChannel channel,
		string[] privileges)
	{
		// extchat.c:1832 — Chan_Can_Priv against the type being SET.
		if (!await PermissionService.ChannelCanPriv(executor, privileges))
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatCannotMakeThatType, executor);
			return new CallState(ErrorMessages.Returns.ChannelPermissionDenied);
		}

		// extchat.c:1836
		if (privileges.HasPriv("Disabled"))
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatChannelWillBeDisabled, executor);
		}

		await Mediator.Send(new UpdateChannelCommand(channel,
			null,
			null,
			Privs: privileges,
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