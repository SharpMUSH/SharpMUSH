using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelMogrifier
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName, MString? obj = null)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await executor.IsGuest())
		{
			await NotifyService.Notify(executor, "CHAT: Guests may not modify channels.", executor);
			return new CallState("#-1 Guests may not modify channels.");
		}

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService, PermissionService, Mediator, NotifyService, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		if (await PermissionService.ChannelCanModifyAsync(executor, channel))
		{
			return new CallState("You cannot modify this channel.");
		}

		var objText = obj?.ToPlainText();
		if (string.IsNullOrEmpty(objText))
		{
			await Mediator.Send(new UpdateChannelCommand(channel,
				null, null, null, null, null, null, null, null, string.Empty, null));
			return new CallState("Channel Mogrifier has been cleared.");
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, objText,
			LocateFlags.All,
			async locate =>
			{
				await Mediator.Send(new UpdateChannelCommand(channel,
					null,
					null,
					null,
					null,
					null,
					null,
					null,
					null,
					locate.Object().DBRef.ToString(),
					null));

				return new CallState("Channel Mogrifier has been updated.");
			});
	}
}