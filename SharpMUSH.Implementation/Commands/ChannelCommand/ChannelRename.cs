using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelRename
{
	public static async ValueTask<CallState> Handle(
		IMUSHCodeParser parser,
		ILocateService LocateService,
		IPermissionService PermissionService,
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

	private static async ValueTask<CallState> RenameAsync(IPermissionService PermissionService, IMediator Mediator,
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

		await NotifyService.Notify(executor, "CHAT: Renamed channel.", executor);
		return new CallState("Renamed channel.");
	}
}