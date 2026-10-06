using Mediator;
using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

/// <summary>
/// Raises <see cref="SharpEvents.ChannelWho"/> when a member comes onto or goes off a channel's member
/// list, so a handler can keep a client's list live (the <c>comm-feed</c> package's <c>comm.who</c>). The
/// list is <c>@channel/who</c>'s (<see cref="ChannelHelper.ChannelMember.ListedAsOn"/>), and each viewer is
/// told only when their own view of it changed: a member hiding on the channel comes and goes for a
/// <c>Priv_Who</c> viewer alone, so nobody else learns anything from the event's timing.
/// </summary>
public class ChannelWhoEventHandler(
	IMediator mediator,
	IEventService eventService,
	IConnectionService connectionService)
	: INotificationHandler<PlayerOnlineChangedNotification>, INotificationHandler<ChannelMembershipChangedNotification>
{
	public async ValueTask Handle(PlayerOnlineChangedNotification notification, CancellationToken cancellationToken)
	{
		var player = notification.Player;
		var number = player.Object().DBRef.Number;
		var cause = notification.Online ? "connect" : "disconnect";

		await foreach (var channel in mediator.CreateStream(new GetOnChannelQuery(player), cancellationToken))
		{
			var membership = await channel.Members.Value
				.FirstOrDefaultAsync(x => x.Member.Object().DBRef.Number == number, cancellationToken);
			if (membership is null)
			{
				continue;
			}

			var hidden = membership.Status.Hide ?? false;
			await RaiseAsync(channel, player, cause,
				before: privileged => !notification.Online && Listed(hidden, privileged),
				after: privileged => notification.Online && Listed(hidden, privileged),
				// A player going off is still counted connected on the handle that is closing; they need no update.
				excludeSubject: !notification.Online);
		}
	}

	public async ValueTask Handle(ChannelMembershipChangedNotification notification, CancellationToken cancellationToken)
	{
		if (notification.Cause is not ("join" or "leave" or "status"))
		{
			return;
		}

		var member = notification.Member;

		// A player who is not connected is on nobody's list before or after. This is also what keeps a
		// database import, which joins every member of every channel, from reading every membership per join.
		var present = member.IsThing || await connectionService.Get(member.Object().DBRef).AnyAsync(cancellationToken);
		if (!present)
		{
			return;
		}

		if (await mediator.Send(new GetChannelQuery(notification.ChannelName), cancellationToken) is not { } channel)
		{
			return;
		}

		var number = member.Object().DBRef.Number;
		var current = await channel.Members.Value
			.FirstOrDefaultAsync(x => x.Member.Object().DBRef.Number == number, cancellationToken);

		bool? hiddenBefore = notification.Cause switch
		{
			"join" => null,
			_ => notification.PreviousStatus is { } previous ? previous.Hide ?? false : null
		};
		bool? hiddenAfter = current is null ? null : current.Status.Hide ?? false;

		await RaiseAsync(channel, member, notification.Cause,
			before: privileged => hiddenBefore is { } hidden && Listed(hidden, privileged),
			after: privileged => hiddenAfter is { } hidden && Listed(hidden, privileged),
			excludeSubject: false);
	}

	/// <summary><see cref="ChannelHelper.ChannelMember.ListedAsOn"/> for a member who is present.</summary>
	private static bool Listed(bool hidden, bool privilegedViewer) => !hidden || privilegedViewer;

	/// <summary>
	/// Tells each connected player member whose view changed: those who list <paramref name="subject"/> now
	/// and did not, <c>on</c>; those who did and no longer do, <c>off</c>.
	/// </summary>
	private async ValueTask RaiseAsync(SharpChannel channel, AnySharpObject subject, string cause,
		Func<bool, bool> before, Func<bool, bool> after, bool excludeSubject)
	{
		var subjectNumber = subject.Object().DBRef.Number;
		var cameOn = new List<string>();
		var wentOff = new List<string>();

		await foreach (var (viewer, _) in channel.Members.Value)
		{
			if (!viewer.IsPlayer
				|| (excludeSubject && viewer.Object().DBRef.Number == subjectNumber)
				|| !await connectionService.Get(viewer.Object().DBRef).AnyAsync())
			{
				continue;
			}

			var privileged = await ChannelHelper.PrivilegedWho(viewer);
			var (was, now) = (before(privileged), after(privileged));
			if (was == now)
			{
				continue;
			}

			(now ? cameOn : wentOff).Add(viewer.Object().DBRef.ToString());
		}

		var channelName = channel.Name.ToPlainText();
		var subjectObjid = subject.Object().DBRef.ToString();
		var subjectName = subject.Object().Name;

		foreach (var (viewers, state) in new[] { (cameOn, "on"), (wentOff, "off") })
		{
			if (viewers.Count == 0)
			{
				continue;
			}

			await eventService.TriggerEventAsync(SharpEvents.ChannelWho,
				subject.Object().DBRef,
				channelName,
				subjectObjid,
				subjectName,
				state,
				string.Join(' ', viewers),
				cause);
		}
	}
}
