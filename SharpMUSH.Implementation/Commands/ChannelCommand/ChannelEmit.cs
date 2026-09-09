using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

/// <summary>
/// PennMUSH <c>do_cemit</c> (<c>src/extchat.c:1622-1690</c>), behind all four of its spellings:
/// <c>@cemit</c>, <c>@nscemit</c>, <c>cemit()</c> and <c>nscemit()</c>. Penn's own
/// <c>fun_cemit</c> (<c>:3445</c>) calls <c>do_cemit</c> rather than reimplementing it, and so does this.
///
/// <para>The gates run in Penn's order, and the See_All + Pemit_All bypass covers all of them — such a
/// player could enumerate the channel's members and <c>@pemit</c> them anyway.</para>
/// </summary>
public static class ChannelEmit
{
	public static async ValueTask<CallState> Handle(
		IPermissionService permissionService,
		IMediator mediator,
		INotifyService notifyService,
		AnySharpObject executor,
		MString channelName,
		MString message,
		bool spoof)
	{
		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(permissionService, mediator,
			notifyService, executor, channelName, notify: true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		var check = await ChannelHelper.CemitRefusal(permissionService, executor, channel);
		if (check.Refused)
		{
			await notifyService.Notify(executor, check.Refusal!, executor);
			return new CallState(ErrorMessages.Returns.ChannelPermissionDenied);
		}

		var membership = await ChannelHelper.ChannelMemberStatus(executor, channel);

		// extchat.c:1667 — the open-channel rule applies to @cemit exactly as it does to @chat, and the
		// same bypass skips it. Membership is not required outright: that is what lets a wizard or a
		// channel-owning object emit onto an open channel it does not listen to.
		if (!check.Overridden && ChannelHelper.OpenChannelRefusal(channel, membership) is { } closed)
		{
			await notifyService.Notify(executor, closed, executor);
			return new CallState(closed);
		}

		if (message.Length == 0)
		{
			await notifyService.Notify(executor, ErrorMessages.Notifications.ChatWhatToEmit, executor);
			return new CallState(ErrorMessages.Returns.NoTextGiven);
		}

		// extchat.c:1685 — CB_NOSPOOF unless the caller asked to spoof AND may.
		var noSpoof = !(spoof && await permissionService.CanNoSpoof(executor));

		await mediator.Publish(new ChannelMessageNotification(
			channel,
			executor.WithNoneOption(),
			noSpoof
				? INotifyService.NotificationType.Emit
				: INotifyService.NotificationType.NSEmit,
			message,
			membership?.Status.Title ?? MarkupText.Empty,
			MarkupText.Plain(executor.Object().Name),
			// An emit carries no speech verb — channel_send's CB_EMIT does not take one. The default
			// format ignores it, but a mogrifier or @chatformat is handed it as %6.
			MarkupText.Empty,
			[]
		));

		return new CallState(string.Empty);
	}
}
