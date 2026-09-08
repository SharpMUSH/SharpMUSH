using Mediator;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

/// <summary>
/// <c>@channel/list</c> — PennMUSH <c>do_channel_list</c> (<c>src/extchat.c:2610-2700</c>).
///
/// <para>This printed one <c>Name: &lt;channel&gt;</c> line per channel and nothing else, so the command
/// that exists to show a channel's population, privileges, locks and your own status on it showed none of
/// them.</para>
/// </summary>
public static class ChannelList
{
	/// <summary>PennMUSH's header, <c>"%-30s %-5s %8s %-16s %-9s %-3s"</c> (<c>src/extchat.c:2622</c>).</summary>
	private static readonly string Header =
		$"{"Name",-30} {"Users",-5} {"Msgs",8} {"Chan Type",-16} {"Status",-9} {"Buf",-3}";

	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService,
		IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService,
		IConnectionService ConnectionService, MString arg0, MString arg1, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var quietSwitch = switches.Contains("QUIET");
		var onSwitch = switches.Contains("ON");
		var offSwitch = switches.Contains("OFF");

		// Switches: On, Off, Or Quiet. On/off are exclusive.
		if (onSwitch && offSwitch)
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.TooManySwitches,
				notifyMessage: "You can only use one of /on or /off.",
				shouldNotify: true);
		}

		// sharpchat.md:181 — "If a <prefix> is given, only channels whose names begin with <prefix> are shown."
		var prefix = arg0.ToPlainText().Trim();

		// Materialised before the per-channel membership reads, as everywhere else that walks the list.
		var all = await Mediator.CreateStream(new GetChannelListQuery()).ToArrayAsync();
		var rows = new List<MString>();
		var names = new List<string>();

		foreach (var channel in all)
		{
			var channelName = channel.Name.ToPlainText();

			// extchat.c:2634 — Chan_Can_See alone here, not the membership fallback: this is a listing, and
			// Penn does not let a member's own presence pull an otherwise unseeable channel into it.
			if (!await PermissionService.ChannelCanSeeAsync(executor, channel)
					|| (prefix.Length != 0 && !channelName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			var status = await ChannelHelper.ChannelMemberStatus(executor, channel);

			if ((onSwitch && status is null) || (offSwitch && status is not null))
			{
				continue;
			}

			if (quietSwitch)
			{
				names.Add(channelName);
				continue;
			}

			var members = await ChannelHelper.ChannelMembers(ConnectionService, channel);
			var messageCount = await Mediator
				.CreateStream(new GetChannelMessagesQuery(channel.Id ?? string.Empty, int.MaxValue))
				.CountAsync();
			var owner = await channel.Owner.WithCancellation(CancellationToken.None);

			rows.Add(MarkupText.Concat([
				channel.Name,
				MarkupText.Plain(new string(' ', Math.Max(30 - channelName.Length, 0))),
				MarkupText.Plain($" {members.Count,5} {messageCount,8}"
												 + $" [{ChannelTypeColumn(channel)} {LockColumn(channel, owner.Object.DBRef.Number == executor.Object().DBRef.Number)}]"
												 + $" [{StatusColumn(status)}] {channel.Buffer,3}")
			]));
		}

		if (quietSwitch)
		{
			// extchat.c:2696 — the quiet form is one line, and says so when nothing matched.
			var quiet = string.Format(ErrorMessages.Notifications.ChatChannelList,
				names.Count == 0 ? ErrorMessages.Notifications.ChatNone : string.Join(", ", names));
			await NotifyService.Notify(executor, quiet, executor);
			return new CallState(quiet);
		}

		// The header prints whether or not anything matched, exactly as Penn's does — it is written before
		// the loop runs.
		var result = MarkupText.Join(MarkupText.NewLine, [MarkupText.Plain(Header), .. rows]);
		await NotifyService.Notify(executor, result, executor);
		return new CallState(result);
	}

	/// <summary>
	/// The seven privilege characters of the "Chan Type" column (<c>src/extchat.c:2673-2679</c>). Admin
	/// and Wizard share a slot, with Admin winning.
	/// </summary>
	private static string ChannelTypeColumn(SharpChannel channel)
	{
		var privs = channel.Privs.ToHashSet(StringComparer.OrdinalIgnoreCase);
		char On(string priv, char letter) => privs.Contains(priv) ? letter : '-';

		return string.Concat(
			On("Disabled", 'D'),
			On("Player", 'P'),
			On("Object", 'T'),
			privs.Contains("Admin") ? 'A' : privs.Contains("Wizard") ? 'W' : '-',
			On("Quiet", 'Q'),
			On("Hide_Ok", 'H'),
			On("Open", 'o'));
	}

	/// <summary>
	/// The five lock characters plus the ownership marker (<c>src/extchat.c:2681-2688</c>). Note Penn's
	/// letter for the SEE lock is 'v', not 's' — 's' is taken by SPEAK.
	/// </summary>
	private static string LockColumn(SharpChannel channel, bool owned)
		=> string.Concat(
			string.IsNullOrEmpty(channel.JoinLock) ? '-' : 'j',
			string.IsNullOrEmpty(channel.SpeakLock) ? '-' : 's',
			string.IsNullOrEmpty(channel.ModLock) ? '-' : 'm',
			string.IsNullOrEmpty(channel.SeeLock) ? '-' : 'v',
			string.IsNullOrEmpty(channel.HideLock) ? '-' : 'h',
			owned ? '*' : '-');

	/// <summary>
	/// The viewer's own standing on the channel (<c>src/extchat.c:2690-2693</c>): Off, Gag or On, then a
	/// character each for their Quiet, Hide and Combine flags.
	/// </summary>
	private static string StatusColumn(SharpChannel.MemberAndStatus? membership)
	{
		if (membership is null)
		{
			return $"{"Off",-3}    ";
		}

		var status = membership.Status;
		var state = (status.Gagged ?? false) ? "Gag" : "On";

		return $"{state,-3} {((status.Mute ?? false) ? 'Q' : ' ')}{((status.Hide ?? false) ? 'H' : ' ')}{((status.Combine ?? false) ? 'C' : ' ')}";
	}
}
