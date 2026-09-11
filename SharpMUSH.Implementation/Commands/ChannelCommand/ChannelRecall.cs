using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

/// <summary>
/// <c>@channel/recall</c> — PennMUSH <c>do_chan_recall</c> (<c>src/extchat.c:3990-4098</c>).
///
/// <para>The buffer is replayed oldest line first, wrapped in the header and footer Penn prints, and each
/// line carries the timestamp it was said at unless <c>/quiet</c> is given.</para>
///
/// <para><c>crecall()</c> shares <see cref="SelectAsync"/> with it. PennMUSH keeps two copies of the
/// argument parsing, the gates and the window arithmetic — <c>do_chan_recall</c> and <c>fun_crecall</c>
/// (<c>:3461</c>) — and they have drifted apart there: the function's access check is
/// <c>Chan_Can_Access</c> where the command's is <c>Guest || !Chan_Can_Join</c>. One implementation, the
/// command's rule, and the callers differ only in how they render what comes back.</para>
/// </summary>
public static class ChannelRecall
{
	private const int DefaultLines = 10;

	/// <summary>
	/// The window of buffered lines a recall should show, or the refusal to give instead. Lines are
	/// oldest first; <see cref="ShowedEverything"/> is Penn's <c>all</c> (<c>:4070</c>), which suppresses
	/// the "use =0" footer.
	/// </summary>
	public readonly record struct RecallWindow(
		SharpChannel Channel,
		List<SharpChannelMessage> Lines,
		bool ShowedEverything);

	/// <summary>
	/// Everything both spellings do before they render: parse the counts, resolve the channel, apply the
	/// access gate, take the window and drop the See_All-only lines the viewer may not read.
	/// </summary>
	public static async ValueTask<RecallSelection> SelectAsync(
		IPermissionService permissionService,
		IMediator mediator,
		INotifyService notifyService,
		AnySharpObject executor,
		MString channelName,
		MString lines,
		MString start,
		bool notify)
	{
		// extchat.c:4029 — both counts are validated before the channel is resolved, so a bad count is
		// answered the same way whether or not the channel exists.
		var requested = DefaultLines;
		if (lines.Length != 0)
		{
			// `=0` means "the whole buffer", exactly as Penn's `num_lines = INT_MAX` does.
			if (!int.TryParse(lines.ToPlainText(), out requested) || requested < 0)
			{
				return await Refuse(notifyService, executor, notify,
					ErrorMessages.Notifications.ChatHowManyLinesToRecall, ErrorMessages.Returns.Integer);
			}

			if (requested == 0)
			{
				requested = int.MaxValue;
			}
		}

		// extchat.c:4008 — the start line is 1-based on the way in.
		var startLine = 0;
		var hasStart = start.Length != 0;
		if (hasStart)
		{
			if (!int.TryParse(start.ToPlainText(), out var parsedStart))
			{
				return await Refuse(notifyService, executor, notify,
					ErrorMessages.Notifications.ChatWhichLineToStartRecall, ErrorMessages.Returns.Integer);
			}

			startLine = Math.Max(parsedStart - 1, 0);
		}

		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(permissionService, mediator,
			notifyService, executor, channelName, notify);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		// extchat.c:4050 — membership is not required; being ABLE to join is. A player who could join the
		// channel may read its history, which is what makes recall usable for deciding whether to join.
		if (!await ChannelHelper.IsMemberOfChannel(executor, channel)
				&& (await executor.IsGuest() || !await permissionService.ChannelCanJoin(executor, channel)))
		{
			return await Refuse(notifyService, executor, notify,
				ErrorMessages.Notifications.ChatMustBeAbleToJoinToRecall, ErrorMessages.Returns.NotAMember);
		}

		var buffered = await mediator
			.CreateStream(new GetChannelMessagesQuery(channel.Id ?? string.Empty, int.MaxValue))
			.ToListAsync();

		// extchat.c:4059 — with no explicit start, recall shows the LAST `requested` lines.
		var effectiveStart = hasStart ? startLine : Math.Max(buffered.Count - requested, 0);

		if (effectiveStart >= buffered.Count)
		{
			return await Refuse(notifyService, executor, notify,
				ErrorMessages.Notifications.ChatNothingToRecall, string.Empty);
		}

		// The window is taken from the buffer and THEN filtered, as Penn does (extchat.c:4083 decrements
		// its line budget for a line it skips): a See_All-only line the viewer may not read costs them one
		// of the lines they asked for rather than pulling an extra one in from further back.
		var selected = await ChannelHelper.FilterRecallableAsync(
			buffered.Skip(effectiveStart).Take(requested), executor);

		return new RecallWindow(channel, selected, effectiveStart == 0 && requested >= buffered.Count);
	}

	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService,
		IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName,
		MString lines, MString start, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var selection = await SelectAsync(PermissionService, Mediator, NotifyService, executor, channelName,
			lines, start, notify: true);

		if (selection is CallState refusal)
		{
			return refusal;
		}

		var (channel, selected, showedEverything) = (RecallWindow)selection.Value!;
		var quiet = switches.Contains("QUIET");
		var channelLabel = channel.Name.ToPlainText();
		var body = selected.Select(x => quiet ? x.Message : Stamped(x));

		// extchat.c:4093 — the "how to see everything" footer is suppressed when everything was already
		// shown, which is exactly when the window started at the top and reached the end.
		MString[] framed =
		[
			MarkupText.Plain(string.Format(ErrorMessages.Notifications.ChatRecallFromChannel, channelLabel)),
			.. body,
			MarkupText.Plain(ErrorMessages.Notifications.ChatEndRecall),
			.. showedEverything
				? Array.Empty<MString>()
				: [MarkupText.Plain(string.Format(ErrorMessages.Notifications.ChatRecallEntireBuffer, channelLabel))]
		];

		var message = MarkupText.Join(MarkupText.NewLine, framed);
		await NotifyService.Notify(executor, message, executor);
		return new CallState(message);
	}

	/// <summary>
	/// PennMUSH <c>do_chan_recall</c>'s <c>"[%s] %s"</c> (<c>src/extchat.c:4088</c>), with
	/// <c>show_time</c>'s stamp.
	/// </summary>
	public static MString Stamped(SharpChannelMessage message)
		=> MarkupText.Concat(
			MarkupText.Plain($"[{TimeFormatting.ShowTime(message.Timestamp)}] "),
			message.Message);

	/// <summary>
	/// A refusal in both registers: the command says it to the player, the function returns it to
	/// softcode. <paramref name="returns"/> empty means the function answers with nothing, which is what
	/// <c>fun_crecall</c> does when there is nothing in the window (<c>src/extchat.c:3548</c>).
	/// </summary>
	private static async ValueTask<CallState> Refuse(INotifyService notifyService, AnySharpObject executor,
		bool notify, string message, string returns)
	{
		if (notify)
		{
			await notifyService.Notify(executor, message, executor);
			return new CallState(message);
		}

		return new CallState(returns);
	}
}
