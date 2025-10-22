using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

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
			await NotifyService.Notify(executor, "CHAT: Guests may not modify channels.");
			return new CallState("#-1 Guests may not modify channels.");
		}

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService, PermissionService, Mediator, NotifyService, channelName, true);
		if (!maybeChannel.IsError)
		{
			await NotifyService.Notify(executor, "CHAT: Channel already exists.");
			return new CallState("#-1 Channel already exists.");
		}

		if (!ChannelHelper.IsValidChannelName(Configuration, channelName))
		{
			await NotifyService.Notify(executor, "Invalid channel name.");
			return new CallState("#-1 Invalid channel name.");
		}

		var allChannels = await Mediator.Send(new GetChannelListQuery());
		var ownedChannels = await allChannels
			.ToAsyncEnumerable()
			.Where(async (x, _) =>
				(await x.Owner.WithCancellation(CancellationToken.None)).Id == executorOwner.Id)
			.CountAsync();

		if (!await executor.IsPriv() && ownedChannels >= Configuration.CurrentValue.Chat.MaxChannels)
		{
			await NotifyService.Notify(executor, "#-1 You have too many channels.");
			return new CallState("#-1 You have too many channels.");
		}

		var parsedPrivileges = ChannelHelper.StringToChannelPrivileges(privileges);
		if (parsedPrivileges.IsError)
		{
			await NotifyService.Notify(executor, $"Invalid privileges: {string.Join(", ", parsedPrivileges.AsError.Value)}.");
			return new CallState("#-1 Invalid privileges.");
		}

		await Mediator.Send(new CreateChannelCommand(channelName, parsedPrivileges.AsPrivileges, executorOwner));

		await NotifyService.Notify(executor, "Channel has been created.");
		return new CallState("Channel has been created.");
	}
}