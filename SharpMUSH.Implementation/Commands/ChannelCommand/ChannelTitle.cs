using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

/// <summary>
/// <c>@channel/title &lt;channel&gt;[=&lt;title&gt;]</c> — PennMUSH <c>do_chan_title</c>
/// (<c>src/extchat.c:3125-3185</c>). It is the speaker's own title, so it requires channel membership
/// rather than channel ownership.
///
/// <para>Three of Penn's four behaviours were missing: asking with no <c>=</c> at all reported your
/// current title rather than clearing it, a title longer than <c>chan_title_len</c> was refused, and a
/// title carrying a newline or a bell was refused. Nothing checked either, so a title could be any length
/// and could carry a newline straight into every channel line you spoke.</para>
/// </summary>
public static class ChannelTitle
{
	public static async ValueTask<CallState> Handle(IMUSHCodeParser parser, ILocateService LocateService,
		IPermissionService PermissionService, IMediator Mediator, INotifyService NotifyService,
		IOptionsWrapper<SharpMUSHOptions> Configuration, MString channelName, MString? title)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var maybeChannel = await ChannelHelper.GetVisibleChannelOrError(PermissionService, Mediator,
			NotifyService, executor, channelName, true);

		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}

		var channel = maybeChannel.AsChannel;
		var channelLabel = channel.Name.ToPlainText();

		var memberStatus = await ChannelHelper.ChannelMemberStatus(executor, channel);
		if (memberStatus is null)
		{
			var notOn = string.Format(ErrorMessages.Notifications.ChatNotOnChannel, channelLabel);
			await NotifyService.Notify(executor, notOn, executor);
			return new CallState(notOn);
		}

		var (_, status) = memberStatus;

		// extchat.c:3145 — no `=` at all is a QUERY. An `=` with nothing after it is a clear.
		if (title is null)
		{
			var current = status.Title?.ToPlainText() ?? string.Empty;
			var answer = current.Length == 0
				? string.Format(ErrorMessages.Notifications.ChatNoTitleSetOn, channelLabel)
				: string.Format(ErrorMessages.Notifications.ChatYourTitleOnIs, channelLabel, current);
			await NotifyService.Notify(executor, answer, executor);
			return new CallState(answer);
		}

		// extchat.c:3160 / :3181 — a NoTitles channel still stores the title and says so, since the
		// privilege can be taken off the channel later.
		var noTitles = channel.Privs.Contains("NoTitles", StringComparer.OrdinalIgnoreCase)
			? "(NoTitles) "
			: string.Empty;

		if (title.Length == 0)
		{
			await Mediator.Send(new UpdateChannelUserStatusCommand(channel, executor,
				status with { Title = MarkupText.Empty }));

			var cleared = string.Format(ErrorMessages.Notifications.ChatTitleCleared, noTitles, channelLabel);
			await NotifyService.Notify(executor, cleared, executor);
			return new CallState(cleared);
		}

		// extchat.c:3165 — the length limit is on the VISIBLE title, so markup does not count against it.
		var plain = title.ToPlainText();

		if (plain.Length > Configuration.CurrentValue.Chat.ChannelTitleLength)
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatTitleTooLong, executor);
			return new CallState(ErrorMessages.Notifications.ChatTitleTooLong);
		}

		// extchat.c:3170 — "Stomp newlines and other weird whitespace": a plain space is the only
		// whitespace a title may carry, and the bell is out too.
		if (plain.Any(x => (char.IsWhiteSpace(x) && x != ' ') || x == '\a'))
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.ChatInvalidCharacterInTitle, executor);
			return new CallState(ErrorMessages.Notifications.ChatInvalidCharacterInTitle);
		}

		await Mediator.Send(new UpdateChannelUserStatusCommand(channel, executor, status with { Title = title }));

		var response = string.Format(ErrorMessages.Notifications.ChatTitleSet, noTitles, channelLabel);
		await NotifyService.Notify(executor, response, executor);
		return new CallState(response);
	}
}
