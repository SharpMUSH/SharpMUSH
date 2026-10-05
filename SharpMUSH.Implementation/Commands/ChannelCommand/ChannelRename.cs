using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelRename
{
	public static async ValueTask<CallState> Handle(
		IMUSHCodeParser parser,
		ILocateService LocateService,
		IChannelPermissionService PermissionService,
		IMediator Mediator,
		INotifyService NotifyService,
		IOptionsWrapper<SharpMUSHOptions> Configuration,
		MString channelName,
		MString newChannelName)
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
			SharpChannel channel => await RenameAsync(PermissionService, Mediator, NotifyService, Configuration, executor, channel,
				newChannelName),
			Error<CallState> error => error.Value
		};
	}

	private static async ValueTask<CallState> RenameAsync(IChannelPermissionService PermissionService, IMediator Mediator,
		INotifyService NotifyService, IOptionsWrapper<SharpMUSHOptions> Configuration, AnySharpObject executor,
		SharpChannel channel, MString newChannelName)
	{
		if (!await PermissionService.ChannelCanModifyAsync(executor, channel))
		{
			await NotifyService.Notify(executor, "You are not the owner of the channel.", executor);
			return new CallState("You are not the owner of the channel.");
		}

		var isValid = ChannelHelper.IsValidChannelName(Configuration, newChannelName);
		if (!isValid)
		{
			await NotifyService.Notify(executor, "CHAT: Invalid channel name.", executor);
			return new CallState(ErrorMessages.Returns.InvalidChannelName);
		}

		// Read before the rename: the channel's identity is its name, so afterwards this object reads the
		// members of a channel that no longer exists.
		var members = await channel.Members.Value.Select(member => member.Member).ToListAsync();

		await Mediator.Send(new UpdateChannelCommand(channel,
			newChannelName,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null,
			null
		));

		// Announced here, after the write's second cache-invalidation pass, rather than from its handler: a
		// channel list cached again between the first pass and the write survives until that second pass,
		// and a listener reading it would be handed the old name (see UpdateChannelCommandHandler).
		foreach (var member in members)
		{
			await Mediator.Publish(new ChannelMembershipChangedNotification(member, newChannelName.ToPlainText(), "rename"));
		}

		await NotifyService.Notify(executor, "CHAT: Renamed channel.", executor);
		return new CallState("Renamed channel.");
	}
}