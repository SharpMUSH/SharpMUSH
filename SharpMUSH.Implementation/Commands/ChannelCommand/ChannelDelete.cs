using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelDelete
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName, MString message)
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
			SharpChannel channel => await DeleteAsync(PermissionService, Mediator, NotifyService, executor, channel),
			Error<CallState> error => error.Value
		};
	}

	private static async ValueTask<CallState> DeleteAsync(IPermissionService PermissionService, IMediator Mediator,
		INotifyService NotifyService, AnySharpObject executor, SharpChannel channel)
	{
		if (!await PermissionService.ChannelCanModifyAsync(executor, channel))
		{
			await NotifyService.Notify(executor, "CHAT: You cannot modify this channel.", executor);
			return new CallState("You cannot modify this channel.");
		}

		await Mediator.Send(new DeleteChannelCommand(channel));

		await NotifyService.Notify(executor, "CHAT: Channel has been deleted.", executor);
		return new CallState("Channel has been deleted.");
	}
}