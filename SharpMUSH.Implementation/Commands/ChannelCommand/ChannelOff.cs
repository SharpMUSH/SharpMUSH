using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

/// <summary>
/// <c>@channel/off</c> — PennMUSH <c>do_channel</c>'s OFF branch (<c>src/extchat.c:1286-1312</c>) when a
/// target is named, and <c>channel_leave_self</c> (<c>:1379-1412</c>) when it is not.
/// </summary>
public static class ChannelOff
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName, MString? arg1)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var target = executor;

		// extchat.c:1294 / :1384 — guests may not leave channels, as they may not join them.
		if (await executor.IsGuest())
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatGuestsCantLeave, executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

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

		// extchat.c:1387 vs :1209 — leaving oneself resolves the name against the channels one is ON, so
		// `@channel/off pub` cannot be made ambiguous by a Public_Announcements channel one never joined.
		var maybeChannel = arg1 is null
			? await SelfLeaveChannel(PermissionService, Mediator, NotifyService, executor, channelName)
			: await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
				NotifyService, executor, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;
		var channelLabel = channel.Name.ToPlainText();

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
			var notOn = string.Format(ErrorMessages.Notifications.ChatTargetNotOnChannel,
				target.Object().Name, channelLabel);
			await NotifyService.Notify(executor, notOn, executor);
			return new CallState(notOn);
		}

		// Channel join/leave announcements are handled by the channel system
		await Mediator.Send(new RemoveUserFromChannelCommand(channel, target));

		if (target.Id() == executor.Id())
		{
			var left = string.Format(ErrorMessages.Notifications.ChatYouLeaveChannel, channelLabel);
			await NotifyService.Notify(executor, left, executor);
			return new CallState(left);
		}

		await NotifyService.Notify(target,
			string.Format(ErrorMessages.Notifications.ChatRemovesYouFromChannel, executor.Object().Name, channelLabel),
			executor);

		var removed = string.Format(ErrorMessages.Notifications.ChatYouRemoveTargetFromChannel,
			target.Object().Name, channelLabel);
		await NotifyService.Notify(executor, removed, executor);
		return new CallState(removed);
	}

	/// <summary>
	/// PennMUSH <c>channel_leave_self</c>'s name resolution (<c>src/extchat.c:1387-1400</c>): match against
	/// the channels the player is on, and when that finds nothing, say "you are not on that channel" if the
	/// name resolves to a visible channel they never joined.
	/// </summary>
	private static async ValueTask<ChannelOrError> SelfLeaveChannel(IPermissionService permissionService,
		IMediator mediator, INotifyService notifyService, AnySharpObject executor, MString channelName)
	{
		var match = await ChannelHelper.MatchChannel(permissionService, mediator, executor, channelName,
			ChannelHelper.ChannelMatchScope.Member);

		if (match.Found)
		{
			return new ChannelOrError(match.Channel!);
		}

		if (match.Kind == ChannelHelper.ChannelMatchKind.Ambiguous)
		{
			await notifyService.Notify(executor, ErrorMessages.Notifications.DontKnowWhichChannel, executor);
			await notifyService.Notify(executor, ChannelHelper.PartialMatchList(match.Candidates), executor);
			return ChannelHelper.AmbiguousChannel();
		}

		var elsewhere = await ChannelHelper.MatchChannel(permissionService, mediator, executor, channelName,
			ChannelHelper.ChannelMatchScope.NonMember);

		if (elsewhere.Found)
		{
			var notOn = string.Format(ErrorMessages.Notifications.ChatNotOnChannel,
				elsewhere.Channel!.Name.ToPlainText());
			await notifyService.Notify(executor, notOn, executor);
			return ChannelHelper.NoSuchChannel(notOn);
		}

		await notifyService.Notify(executor, ErrorMessages.Notifications.DontRecognizeThatChannel, executor);
		return ChannelHelper.NoSuchChannel();
	}
}
