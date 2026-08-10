using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

/// <summary>
/// <c>@channel/on</c> — PennMUSH <c>do_channel</c>'s ON branch (<c>src/extchat.c:1240-1284</c>) when a
/// target is named, and <c>channel_join_self</c> (<c>:1330-1375</c>) when it is not.
///
/// <para>Before this, joining ran straight from "does the channel exist?" to
/// <c>AddUserToChannelCommand</c>: no type gate, no privilege gate, no join lock, no disabled check and
/// no control check on a named target. A mortal could join themselves — or anyone else — to a wizard-only
/// channel, a disabled channel, or a channel whose join lock they failed.</para>
/// </summary>
public static class ChannelOn
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

		// extchat.c:1345 — a channel the joiner cannot see is not a channel they can join.
		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(parser, PermissionService, Mediator,
			NotifyService, executor, channelName, true);
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		// extchat.c:1245 — guests may not join channels at all.
		if (await executor.IsGuest())
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatGuestsCantJoin, executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		// extchat.c:1250 — joining somebody else to a channel requires control of them.
		if (target.Id() != executor.Id() && !await PermissionService.Controls(executor, target))
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatInvalidTarget, executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (await ChannelHelper.IsMemberOfChannel(target, channel))
		{
			var alreadyOn = $"CHAT: {target.Object().Name} is already on {channel.Name.ToPlainText()}.";
			await NotifyService.Notify(executor, alreadyOn, executor);
			return new CallState(alreadyOn);
		}

		var joinCheck = await ChannelHelper.JoinRefusal(PermissionService, executor, target, channel);
		if (joinCheck.Refused)
		{
			await NotifyService.Notify(executor, joinCheck.Refusal!, executor);
			return new CallState(ErrorMessages.Returns.ChannelPermissionDenied);
		}

		if (joinCheck.Warning is not null)
		{
			await NotifyService.Notify(executor, joinCheck.Warning, executor);
		}

		// Channel join/leave announcements are handled by the channel system
		await Mediator.Send(new AddUserToChannelCommand(channel, target));

		await NotifyService.Notify(executor, $"CHAT: {target.Object().Name} has been added to {channelName}.", executor);
		return new CallState($"{target.Object().Name} has been added to {channelName}.");
	}
}
