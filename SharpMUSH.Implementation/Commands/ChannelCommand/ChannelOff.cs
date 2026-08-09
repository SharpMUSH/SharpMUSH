using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

public static class ChannelOff
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName, MString? arg1)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var target = executor;

		if (arg1 is not null)
		{
			var targetName = arg1.ToPlainText();

			var maybeTarget =
				await LocateService.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, targetName);

			switch (maybeTarget)
			{
				case { IsError: true }:
					return new CallState(maybeTarget.AsError.Value);
				case { IsNone: true }:
					return new CallState(ErrorMessages.Returns.PlayerNotFound);
			}

			target = maybeTarget.AsAnyObject;
		}

		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, Mediator, NotifyService, channelName, true);
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		// extchat.c:1289 — "You must control either the victim or the channel". Without this, any mortal
		// could remove any other player from any channel.
		if (target.Id() != executor.Id()
				&& !await PermissionService.Controls(executor, target)
				&& !await PermissionService.ChannelCanModifyAsync(executor, channel))
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatInvalidTarget, executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (!await ChannelHelper.IsMemberOfChannel(target, channel))
		{
			var notOn = $"CHAT: {target.Object().Name} is not on {channel.Name.ToPlainText()}.";
			await NotifyService.Notify(executor, notOn, executor);
			return new CallState(notOn);
		}

		// Channel join/leave announcements are handled by the channel system
		await Mediator.Send(new RemoveUserFromChannelCommand(channel, target));

		await NotifyService.Notify(executor, $"CHAT: {target.Object().Name} has been removed from {channelName}.", executor);
		return new CallState($"{target.Object().Name} has been removed from {channelName}.");
	}
}