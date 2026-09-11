using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

/// <summary>
/// <c>@channel/on</c> — PennMUSH <c>do_channel</c>'s ON branch (<c>src/extchat.c:1240-1284</c>) when a
/// target is named, and <c>channel_join_self</c> (<c>:1330-1375</c>) when it is not.
///
/// <para>A join passes the type gate, the privilege gate, the join lock and the disabled check; joining
/// somebody else additionally requires control of them.</para>
/// </summary>
public static class ChannelOn
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService, IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName, MString? arg1)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var target = executor;

		// extchat.c:1245 / :1334 — guests may not join channels at all, whoever they aim at.
		if (await executor.IsGuest())
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatGuestsCantJoin, executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (arg1 is not null)
		{
			var targetName = arg1.ToPlainText();

			switch (await LocateService.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, targetName))
			{
				case AnySharpObject found:
					target = found;
					break;
				case None:
					return new CallState(ErrorMessages.Returns.PlayerNotFound);
				case Error<string> error:
					return new CallState(error.Value);
			}
		}

		// extchat.c:1328 vs :1209 — joining SOMEBODY ELSE resolves the name against every visible channel,
		// but joining oneself resolves it against the channels one is not already on. The narrower scope is
		// what makes `@channel/on pub` unambiguous for a player who is on Public already, and it is the only
		// way the "you are already on" answer below is reachable.
		var maybeChannel = arg1 is null
			? await SelfJoinChannel(PermissionService, Mediator, NotifyService, executor, channelName)
			: await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
				NotifyService, executor, channelName, true);

		return maybeChannel switch
		{
			SharpChannel channel => await JoinAsync(PermissionService, Mediator, NotifyService, executor, target, channel),
			Error<CallState> error => error.Value
		};
	}

	/// <summary>Puts <paramref name="target"/> on the channel, if the executor may and it passes the channel's gates.</summary>
	private static async ValueTask<CallState> JoinAsync(IPermissionService PermissionService, IMediator Mediator,
		INotifyService NotifyService, AnySharpObject executor, AnySharpObject target, SharpChannel channel)
	{
		var channelLabel = channel.Name.ToPlainText();

		// extchat.c:1250 — joining somebody else to a channel requires control of them.
		if (target.Id() != executor.Id() && !await PermissionService.Controls(executor, target))
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatInvalidTarget, executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (await ChannelHelper.IsMemberOfChannel(target, channel))
		{
			var alreadyOn = string.Format(ErrorMessages.Notifications.ChatTargetAlreadyOnChannel,
				target.Object().Name, channelLabel);
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

		// extchat.c:1272 / :1367 — the confirmation names the CHANNEL, not the abbreviation that was
		// typed, so a player who joins with `@channel/on pub` is told which channel they landed on.
		if (target.Id() == executor.Id())
		{
			var joined = string.Format(ErrorMessages.Notifications.ChatYouJoinChannel, channelLabel);
			await NotifyService.Notify(executor, joined, executor);
			return new CallState(joined);
		}

		await NotifyService.Notify(target,
			string.Format(ErrorMessages.Notifications.ChatJoinsYouToChannel, executor.Object().Name, channelLabel),
			executor);

		var joinedTarget = string.Format(ErrorMessages.Notifications.ChatYouJoinTargetToChannel,
			target.Object().Name, channelLabel);
		await NotifyService.Notify(executor, joinedTarget, executor);
		return new CallState(joinedTarget);
	}

	/// <summary>
	/// PennMUSH <c>channel_join_self</c>'s name resolution (<c>src/extchat.c:1328-1345</c>): match against
	/// the channels the joiner is NOT on, and when that finds nothing, check whether the name names a
	/// channel they are already on so the refusal can say so instead of denying the channel exists.
	/// </summary>
	private static async ValueTask<ChannelOrError> SelfJoinChannel(IPermissionService permissionService,
		IMediator mediator, INotifyService notifyService, AnySharpObject executor, MString channelName)
	{
		var match = await ChannelHelper.MatchChannel(permissionService, mediator, executor, channelName,
			ChannelHelper.ChannelMatchScope.NonMember);

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

		var already = await ChannelHelper.MatchChannel(permissionService, mediator, executor, channelName,
			ChannelHelper.ChannelMatchScope.Member);

		if (already.Found)
		{
			var alreadyOn = string.Format(ErrorMessages.Notifications.ChatAlreadyOnChannel,
				already.Channel!.Name.ToPlainText());
			await notifyService.Notify(executor, alreadyOn, executor);
			return ChannelHelper.NoSuchChannel(alreadyOn);
		}

		await notifyService.Notify(executor, ErrorMessages.Notifications.DontRecognizeThatChannel, executor);
		return ChannelHelper.NoSuchChannel();
	}
}
