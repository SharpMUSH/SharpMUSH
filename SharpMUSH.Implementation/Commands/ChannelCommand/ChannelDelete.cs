using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
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

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService, PermissionService, Mediator, NotifyService, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		// The sense of this check was inverted: whoever COULD modify the channel was refused, and
		// whoever could not fell through and deleted it.
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