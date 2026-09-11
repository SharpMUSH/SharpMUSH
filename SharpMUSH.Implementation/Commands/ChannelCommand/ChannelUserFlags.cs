using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

/// <summary>
/// <c>@channel/gag</c>, <c>/mute</c>, <c>/hide</c> and <c>/combine</c> and their four <c>un</c>
/// counterparts — PennMUSH <c>do_chan_user_flags</c> (<c>src/extchat.c:1900-2050</c>), which is one
/// function for all eight because they are one operation with a different bit. The <c>un</c> forms are
/// the same call with "n" for an answer, and the channel is optional throughout: omitted, the switch acts
/// on every channel the caller is on.
///
/// <para>All four are the CALLER's own settings. <c>/mute</c> sets <c>CU_QUIET</c> on the caller, which
/// suppresses that channel's connect and disconnect announcements; PennMUSH has no command for muting
/// somebody else, and <c>@clock/speak</c> is what stops a member speaking.</para>
/// </summary>
public static class ChannelUserFlags
{
	public enum UserFlag
	{
		/// <summary>PennMUSH <c>CU_QUIET</c> — <c>@channel/mute</c>.</summary>
		Quiet,

		/// <summary>PennMUSH <c>CU_HIDE</c>.</summary>
		Hide,

		/// <summary>PennMUSH <c>CU_GAG</c>.</summary>
		Gag,

		/// <summary>PennMUSH <c>CU_COMBINE</c>.</summary>
		Combine
	}

	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, IPermissionService PermissionService,
		IMediator Mediator, INotifyService NotifyService, MString? channelName, MString? yesNo, UserFlag flag,
		bool forceOff)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// extchat.c:1908 — combining connect announcements across channels is a player-only option.
		if (flag == UserFlag.Combine && !executor.IsPlayer)
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatOnlyPlayersCanUseThat, executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		// extchat.c:1910 — `abs(yesno(isyn))`: yes/on/1/true set the flag, no/off/0/false clear it. The
		// un-switches are Penn's own shorthand for passing "n" (cmd_channel, :3628-3640).
		bool setting;
		if (forceOff)
		{
			setting = false;
		}
		else
		{
			switch (yesNo?.ToPlainText().Trim())
			{
				case null or "":
					setting = true;
					break;
				case var text when text.StartsWith('y') || text.StartsWith('Y')
													 || text.Equals("on", StringComparison.OrdinalIgnoreCase)
													 || text is "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase):
					setting = true;
					break;
				case var text when text.StartsWith('n') || text.StartsWith('N')
													 || text.Equals("off", StringComparison.OrdinalIgnoreCase)
													 || text is "0" || text.Equals("false", StringComparison.OrdinalIgnoreCase):
					setting = false;
					break;
				default:
					await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatYesOrNoOnly, executor);
					return new CallState(ErrorMessages.Returns.InvalidOption);
			}
		}

		if (channelName is null || channelName.Length == 0)
		{
			// extchat.c:1913 — the bulk form walks the executor's OWN channel list, not every channel in the
			// game, and says one thing about the lot of them rather than naming each one.
			var channels = await Mediator.CreateStream(new GetOnChannelQuery(executor)).ToArrayAsync();

			if (channels.Length == 0)
			{
				await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatNotOnAnyChannels, executor);
				return new CallState(ErrorMessages.Notifications.ChatNotOnAnyChannels);
			}

			await NotifyService.Notify(executor, BulkSummary(flag, setting), executor);
			return await SetFlagAsync(PermissionService, Mediator, NotifyService, executor, channels, flag, setting,
				silent: true);
		}

		// extchat.c:1938 — test_channel_on: these all act on a channel you are ON, so the name resolves
		// against those and nothing else.
		return await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, notify: true,
			scope: ChannelHelper.ChannelMatchScope.Member) switch
		{
			SharpChannel channel => await SetFlagAsync(PermissionService, Mediator, NotifyService, executor, [channel],
				flag, setting, silent: false),
			Error<CallState> error => error.Value
		};
	}

	/// <summary>
	/// Sets or clears <paramref name="flag"/> on each channel the executor is on; <paramref name="silent"/> is
	/// the bulk form, which has already said what it did.
	/// </summary>
	private static async ValueTask<CallState> SetFlagAsync(IPermissionService PermissionService, IMediator Mediator,
		INotifyService NotifyService, AnySharpObject executor, SharpChannel[] channels, UserFlag flag, bool setting,
		bool silent)
	{
		var changed = 0;

		foreach (var channel in channels)
		{
			var membership = await ChannelHelper.ChannelMemberStatus(executor, channel);

			if (membership is null)
			{
				if (!silent)
				{
					await NotifyService.Notify(executor,
						string.Format(ErrorMessages.Notifications.ChatNotOnChannel, channel.Name.ToPlainText()), executor);
				}

				continue;
			}

			// extchat.c:2001 — the Hide_Ok privilege and the hide lock decide who may vanish from a
			// channel's who-list. A wizard overrides both.
			if (flag == UserFlag.Hide && setting
					&& !await PermissionService.ChannelCanHide(executor, channel) && !await executor.IsWizard())
			{
				if (!silent)
				{
					await NotifyService.Notify(executor,
						string.Format(ErrorMessages.Notifications.ChatCannotHideOnChannel, channel.Name.ToPlainText()),
						executor);
				}

				continue;
			}

			await Mediator.Send(new UpdateChannelUserStatusCommand(channel, executor, flag switch
			{
				UserFlag.Quiet => new SharpChannelStatus(null, null, null, setting, null),
				UserFlag.Hide => new SharpChannelStatus(null, null, setting, null, null),
				UserFlag.Gag => new SharpChannelStatus(null, setting, null, null, null),
				_ => new SharpChannelStatus(setting, null, null, null, null)
			}));

			changed++;

			if (!silent)
			{
				await NotifyService.Notify(executor,
					string.Format(PerChannelMessage(flag, setting), channel.Name.ToPlainText()), executor);
			}
		}

		return new CallState(changed);
	}

	private static string BulkSummary(UserFlag flag, bool setting) => (flag, setting) switch
	{
		(UserFlag.Quiet, true) => ErrorMessages.Notifications.ChatAllChannelsMuted,
		(UserFlag.Quiet, false) => ErrorMessages.Notifications.ChatAllChannelsUnmuted,
		(UserFlag.Hide, true) => ErrorMessages.Notifications.ChatHideOnAllChannels,
		(UserFlag.Hide, false) => ErrorMessages.Notifications.ChatUnhideOnAllChannels,
		(UserFlag.Gag, true) => ErrorMessages.Notifications.ChatAllChannelsGagged,
		(UserFlag.Gag, false) => ErrorMessages.Notifications.ChatAllChannelsUngagged,
		(UserFlag.Combine, true) => ErrorMessages.Notifications.ChatAllChannelsCombined,
		_ => ErrorMessages.Notifications.ChatAllChannelsUncombined
	};

	private static string PerChannelMessage(UserFlag flag, bool setting) => (flag, setting) switch
	{
		(UserFlag.Quiet, true) => ErrorMessages.Notifications.ChatNoLongerHearConnections,
		(UserFlag.Quiet, false) => ErrorMessages.Notifications.ChatNowHearConnections,
		(UserFlag.Hide, true) => ErrorMessages.Notifications.ChatNoLongerOnWhoList,
		(UserFlag.Hide, false) => ErrorMessages.Notifications.ChatNowOnWhoList,
		(UserFlag.Gag, true) => ErrorMessages.Notifications.ChatNoLongerHearMessages,
		(UserFlag.Gag, false) => ErrorMessages.Notifications.ChatNowHearMessages,
		(UserFlag.Combine, true) => ErrorMessages.Notifications.ChatConnectionsNowCombined,
		_ => ErrorMessages.Notifications.ChatConnectionsNoLongerCombined
	};
}
