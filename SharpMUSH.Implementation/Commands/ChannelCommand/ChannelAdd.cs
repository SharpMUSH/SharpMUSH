using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelAdd
{
	public static async ValueTask<CallState> Handle(
		IMUSHCodeParser parser,
		ILocateService LocateService,
		IPermissionService PermissionService,
		IMediator Mediator,
		INotifyService NotifyService,
		IOptionsWrapper<SharpMUSHOptions> Configuration, MString channelName, MString privileges)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var executorOwner = await executor.Object().Owner.WithCancellation(CancellationToken.None);
		if (await executor.IsGuest())
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatGuestsCantModify, executor);
			return new CallState(ErrorMessages.Returns.GuestsCannotModifyChannels);
		}

		// notify: false — a missing channel is the expected case when creating one; the player should
		// not be told "Channel not found." on their way to "Channel has been created."
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, Mediator, NotifyService, channelName, false);
		if (!maybeChannel.IsError)
		{
			await NotifyService.Notify(executor, "CHAT: Channel already exists.", executor);
			return new CallState(ErrorMessages.Returns.ChannelAlreadyExists);
		}

		if (!ChannelHelper.IsValidChannelName(Configuration, channelName))
		{
			await NotifyService.Notify(executor, "Invalid channel name.", executor);
			return new CallState(ErrorMessages.Returns.InvalidChannelName);
		}

		var allChannels = Mediator.CreateStream(new GetChannelListQuery());
		var ownedChannels = await allChannels
			.Where(async (x, _) =>
				(await x.Owner.WithCancellation(CancellationToken.None)).Id == executorOwner.Id)
			.CountAsync();

		if (!await executor.IsPriv() && ownedChannels >= Configuration.CurrentValue.Chat.MaxChannels)
		{
			await NotifyService.Notify(executor, ErrorMessages.Returns.TooManyChannels, executor);
			return new CallState(ErrorMessages.Returns.TooManyChannels);
		}

		var parsedPrivileges = ChannelHelper.StringToChannelPrivileges(privileges, []);
		if (parsedPrivileges.IsError)
		{
			await NotifyService.Notify(executor, $"Invalid privileges: {string.Join(", ", parsedPrivileges.AsError.Value)}.", executor);
			return new CallState(ErrorMessages.Returns.InvalidPrivileges);
		}

		// extchat.c:1736 — `if (!Chan_Can(player, type))`. You cannot create a channel of a type you could
		// not yourself use, which includes a DISABLED one: Chan_Can is false for that bit for everybody,
		// wizards included. A wizard disables an existing channel through @channel/privs instead, where
		// Chan_Can_Priv's `Wizard(p) ||` escape applies.
		if (!await PermissionService.ChannelStandardCan(executor, parsedPrivileges.AsPrivileges))
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatCannotCreateThatType, executor);
			return new CallState(ErrorMessages.Returns.ChannelPermissionDenied);
		}

		await Mediator.Send(new CreateChannelCommand(channelName, parsedPrivileges.AsPrivileges, executorOwner));

		await NotifyService.Notify(executor, "Channel has been created.", executor);
		return new CallState("Channel has been created.");
	}
}