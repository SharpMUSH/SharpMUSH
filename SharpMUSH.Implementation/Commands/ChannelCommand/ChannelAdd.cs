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

		// RAW lookup on purpose — do not "fix" this to GetVisibleChannelOrError.
		//
		// Channel names are globally unique, so uniqueness has to be tested against every channel, not just
		// the ones this player may see. A visible lookup here would report "no such channel" for a hidden
		// one and then let the create proceed — on ArangoDB and Memgraph a duplicate, on SurrealDB a hidden
		// channel created over by someone who cannot see it. PennMUSH resolves it the same way:
		// ok_channel_name (src/extchat.c:1855-1870) walks the whole channel list and returns
		// NAME_NOT_UNIQUE without consulting Chan_Can_See.
		//
		// This check is a fast path for the message, NOT the guarantee: it cannot be atomic with a create
		// that happens several awaits later. CreateChannelAsync decides uniqueness inside its own storage
		// transaction and answers ChannelNameTaken, which is handled below.
		//
		// The residual leak is that "CHAT: Channel already exists." tells a player a name is taken even
		// when they cannot see the channel holding it. That is inherent to a global namespace and PennMUSH
		// leaks it identically. notify: false keeps the lookup itself silent.
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, Mediator, NotifyService, channelName, false);
		if (!maybeChannel.IsError)
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatAlreadyExists, executor);
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

		// The check above raced; this one cannot. CreateChannelAsync tests the name inside the same storage
		// transaction that writes the channel, so the loser is refused rather than duplicating the winner
		// (ArangoDB, Memgraph) or overwriting its privileges and locks (SurrealDB).
		var creation = await Mediator.Send(new CreateChannelCommand(channelName, parsedPrivileges.AsPrivileges, executorOwner));

		if (creation.IsNameTaken)
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatAlreadyExists, executor);
			return new CallState(ErrorMessages.Returns.ChannelAlreadyExists);
		}

		if (creation.IsError)
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatChannelCreationFailed, executor);
			return new CallState(ErrorMessages.Returns.ChannelCreationFailed);
		}

		await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatChannelCreated, executor);
		return new CallState(ErrorMessages.Notifications.ChatChannelCreated);
	}
}