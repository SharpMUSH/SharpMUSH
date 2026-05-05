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

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService, PermissionService, Mediator, NotifyService, channelName, true);
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

		var parsedPrivileges = ChannelHelper.StringToChannelPrivileges(privileges);
		if (parsedPrivileges.IsError)
		{
			await NotifyService.Notify(executor, $"Invalid privileges: {string.Join(", ", parsedPrivileges.AsError.Value)}.", executor);
			return new CallState(ErrorMessages.Returns.InvalidPrivileges);
		}

		await Mediator.Send(new CreateChannelCommand(channelName, parsedPrivileges.AsPrivileges, executorOwner));

		await NotifyService.Notify(executor, "Channel has been created.", executor);
		return new CallState("Channel has been created.");
	}
}