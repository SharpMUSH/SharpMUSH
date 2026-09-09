using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
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
/// </summary>
public static class ChannelRecall
{
	private const int DefaultLines = 10;

	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService,
		IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService, MString channelName,
		MString lines, MString start, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// extchat.c:4029 — both count and start are validated before the channel is resolved, so a bad
		// count is answered the same way whether or not the channel exists.
		var requested = DefaultLines;
		if (lines.Length != 0)
		{
			// `=0` means "the whole buffer", exactly as Penn's `num_lines = INT_MAX` does.
			if (!int.TryParse(lines.ToPlainText(), out requested) || requested < 0)
			{
				await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatHowManyLinesToRecall, executor);
				return new CallState(ErrorMessages.Notifications.ChatHowManyLinesToRecall);
			}

			if (requested == 0)
			{
				requested = int.MaxValue;
			}
		}

		// extchat.c:4008 — the start line is 1-based on the way in.
		var startLine = 0;
		if (start.Length != 0)
		{
			if (!int.TryParse(start.ToPlainText(), out var parsedStart))
			{
				await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatWhichLineToStartRecall, executor);
				return new CallState(ErrorMessages.Notifications.ChatWhichLineToStartRecall);
			}

			startLine = Math.Max(parsedStart - 1, 0);
		}

		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;

		// extchat.c:4050 — membership is not required; being ABLE to join is. A player who could join the
		// channel may read its history, which is what makes recall usable for deciding whether to join.
		if (!await ChannelHelper.IsMemberOfChannel(executor, channel)
				&& (await executor.IsGuest() || !await PermissionService.ChannelCanJoin(executor, channel)))
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatMustBeAbleToJoinToRecall, executor);
			return new CallState(ErrorMessages.Returns.ChannelPermissionDenied);
		}

		var buffered = await Mediator.CreateStream(new GetChannelMessagesQuery(channel.Id ?? string.Empty, int.MaxValue))
			.ToListAsync();

		// extchat.c:4059 — with no explicit start, recall shows the LAST `requested` lines.
		var effectiveStart = start.Length != 0
			? startLine
			: Math.Max(buffered.Count - requested, 0);

		if (effectiveStart >= buffered.Count)
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatNothingToRecall, executor);
			return new CallState(ErrorMessages.Notifications.ChatNothingToRecall);
		}

		// The window is taken from the buffer and THEN filtered, as Penn does (extchat.c:4083 decrements
		// its line budget for a line it skips): a See_All-only line the viewer may not read costs them one
		// of the lines they asked for rather than pulling an extra one in from further back.
		var selected = await ChannelHelper.FilterRecallableAsync(
			buffered.Skip(effectiveStart).Take(requested), executor);

		var quiet = switches.Contains("QUIET");
		var channelLabel = channel.Name.ToPlainText();
		var body = selected.Select(x => quiet ? x.Message : Stamped(x));

		// extchat.c:4093 — the "how to see everything" footer is suppressed when everything was already
		// shown, which is exactly when the window started at the top and reached the end.
		var showedEverything = effectiveStart == 0 && requested >= buffered.Count;

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
	private static MString Stamped(SharpChannelMessage message)
		=> MarkupText.Concat(
			MarkupText.Plain($"[{TimeFormatting.ShowTime(message.Timestamp)}] "),
			message.Message);
}
