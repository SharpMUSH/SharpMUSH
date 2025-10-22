using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

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
			await NotifyService.Notify(executor, "CHAT: Guests may not modify channels.");
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
			return new CallState("You are not the owner of the channel.");
		}

		var isValid = ChannelHelper.IsValidChannelName(Configuration, newChannelName);
		if (!isValid)
		{
			await NotifyService.Notify(executor, "CHAT: Invalid channel name.");
			return new CallState("#-1 CHAT: Invalid channel name.");
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

		await NotifyService.Notify(executor, "CHAT: Renamed channel.");
		return new CallState("Renamed channel.");
	}
}