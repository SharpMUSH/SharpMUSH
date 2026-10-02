using SharpMUSH.Implementation.Commands.MailCommand;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// Parse message specification (e.g. "123" or "INBOX:5") into folder and message index
	/// </summary>
	private async ValueTask<(string folder, int messageIndex)> ParseMessageSpec(
		IMUSHCodeParser parser,
		AnySharpObject player,
		string messageSpec)
	{
		var parts = messageSpec.Split(':', 2);
		string folder;
		int messageIndex;

		if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
		{
			folder = parts[0].Trim().ToUpper();
			if (!int.TryParse(parts[1].Trim(), out messageIndex) || messageIndex < 1)
			{
				return (folder, -1);
			}
		}
		else
		{
			folder = await MessageListHelper.CurrentMailFolder(parser, ObjectDataService, player);
			if (!int.TryParse(messageSpec.Trim(), out messageIndex) || messageIndex < 1)
			{
				return (folder, -1);
			}
		}

		// Convert from 1-indexed to 0-indexed
		return (folder, messageIndex - 1);
	}

	/// <summary>
	/// Retrieve mail message by folder and index
	/// </summary>
	private async ValueTask<SharpMail?> GetMailMessage(
		AnySharpObject player,
		string folder,
		int messageIndex)
		=> player is SharpPlayer mailbox
			? await Mediator.Send(new GetMailQuery(mailbox, messageIndex, folder))
			: null;

	/// <summary>
	/// PennMUSH <c>mailfun_fetch</c> (<c>src/extmail.c:2154-2186</c>): the message
	/// <c>[&lt;player&gt;, ][&lt;folder&gt;:]&lt;message&gt;</c> names, or null. With one argument it reads
	/// the caller's own mailbox and says nothing when the message is not there. With two it matches the
	/// player noisily, tells a caller who does not control them "Permission denied", and a malformed
	/// message "Invalid message specification"; a well-formed number with no message behind it is
	/// silent either way. Each function then returns its own failure value.
	/// </summary>
	private async ValueTask<SharpMail?> FetchMailAsync(IMUSHCodeParser parser, AnySharpObject executor,
		Dictionary<string, CallState> args)
	{
		if (args.Count == 1)
		{
			var (ownFolder, ownIndex) = await ParseMessageSpec(parser, executor, args["0"].Message!.ToPlainText());
			return ownIndex < 0 ? null : await GetMailMessage(executor, ownFolder, ownIndex);
		}

		if (await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor,
				args["0"].Message!.ToPlainText(), LocateFlags.PlayersPreference) is not AnySharpObject found)
		{
			return null;
		}

		if (found is not SharpPlayer player)
		{
			// Penn matches TYPE_PLAYER, so anything else is no match at all, and said the same way.
			await NotifyService.Notify(executor, ErrorMessages.Notifications.CantSeeThat, executor);
			return null;
		}

		if (!await CanReadMailOf(executor, player))
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.MailFetchPermissionDenied, executor);
			return null;
		}

		var (folder, index) = await ParseMessageSpec(parser, player, args["1"].Message!.ToPlainText());
		if (index < 0)
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.MailInvalidMessageSpecification, executor);
			return null;
		}

		return await GetMailMessage(player, folder, index);
	}

	/// <summary>
	/// Whether <paramref name="executor"/> may read <paramref name="target"/>'s mailbox. PennMUSH asks
	/// <c>controls(executor, player)</c> once the player is matched (<c>src/extmail.c</c> fun_mail,
	/// fun_maillist, mailfun_fetch), so a player may always read their own.
	/// </summary>
	private ValueTask<bool> CanReadMailOf(AnySharpObject executor, AnySharpObject target)
		=> PermissionService.Controls(executor, target);

	/// <summary>
	/// The counts the mail statistics functions report, taken in one pass over a mailbox. As in PennMUSH's
	/// <c>count_mail</c>, a cleared message counts only as cleared, and an uncleared one as read or unread.
	/// </summary>
	private readonly record struct MailTally(int Total, int Unread, int Cleared, int Bytes)
	{
		public int Read => Total - Unread - Cleared;
	}

	private static ValueTask<MailTally> TallyMail(IAsyncEnumerable<SharpMail> mail)
		=> mail.AggregateAsync(new MailTally(), (tally, m) => new MailTally(
			tally.Total + 1,
			tally.Unread + (!m.Cleared && !m.Read ? 1 : 0),
			tally.Cleared + (m.Cleared ? 1 : 0),
			tally.Bytes + m.Content.Length));

	[SharpFunction(Name = "mail", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["player", "status"])]
	public async ValueTask<CallState> mail(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (args.Count == 0 || (args.Count == 1 && string.IsNullOrWhiteSpace(args["0"].Message?.ToPlainText())))
		{
			// Only a player has a mailbox; anything else holds no mail.
			var count = executor is SharpPlayer own
				? await Mediator.CreateStream(new GetAllMailListQuery(own)).CountAsync()
				: 0;
			return new CallState(count.ToString());
		}

		// fun_mail (src/extmail.c:2121-2137) tries mail(<player>) first, matching quietly; anything that
		// is not a player falls through to the message fetch.
		if (args.Count == 1 && !IsMessageNumber(args["0"].Message!.ToPlainText())
				&& await LocateService.Locate(parser, executor, executor, args["0"].Message!.ToPlainText(),
					LocateFlags.PlayersPreference) is AnySharpObject and SharpPlayer mailbox)
		{
			if (!await CanReadMailOf(executor, mailbox))
			{
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var tally = await TallyMail(Mediator.CreateStream(new GetAllMailListQuery(mailbox)));
			return new CallState($"{tally.Read} {tally.Unread} {tally.Cleared}");
		}

		return await FetchMailAsync(parser, executor, args) is { } mail
			? new CallState(mail.Content)
			: new CallState(ErrorMessages.Returns.InvalidMessageOrPlayer);
	}

	/// <summary>
	/// Check if a string is a valid message number (e.g., "123" or "INBOX:5")
	/// </summary>
	private bool IsMessageNumber(string arg)
	{
		if (string.IsNullOrEmpty(arg))
		{
			return false;
		}

		// "123", or "FOLDER:123": a folder before the first colon, the number after it.
		var text = arg.AsSpan();
		Span<System.Range> parts = stackalloc System.Range[2];
		return text.Split(parts, ':') switch
		{
			1 => !text.ContainsAnyExceptInRange('0', '9'),
			_ => !text[parts[0]].IsWhiteSpace() && !text[parts[1]].ContainsAnyExceptInRange('0', '9')
		};
	}
	[SharpFunction(Name = "maillist", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["folder", "flags"])]
	public async ValueTask<CallState> maillist(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		AnySharpObject targetPlayer = executor;
		string? messageListSpec = null;

		if (args.Count == 1)
		{
			messageListSpec = args["0"].Message?.ToPlainText();
		}
		else if (args.Count == 2)
		{
			// fun_maillist (src/extmail.c:817-821) matches quietly and answers anything but a player with #-1 NO MATCH.
			var playerArg = args["0"].Message!.ToPlainText()!;
			if (await LocateService.Locate(parser, executor, executor, playerArg, LocateFlags.PlayersPreference)
				is not (AnySharpObject and SharpPlayer located))
			{
				return new CallState(ErrorMessages.Returns.NoMatch);
			}

			if (!await CanReadMailOf(executor, located))
			{
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			targetPlayer = located;
			messageListSpec = args["1"].Message?.ToPlainText();
		}

		// Only a player has a mailbox; anything else holds no mail.
		if (targetPlayer is not SharpPlayer mailbox)
		{
			return new CallState(string.Empty);
		}

		var msgListArg = messageListSpec != null ? MarkupText.Plain(messageListSpec) : null;
		var filteredList = await MessageListHelper.Handle(
			parser, ObjectDataService, Mediator, NotifyService, msgListArg, targetPlayer);

		return filteredList switch
		{
			Error<string> error => new CallState(string.Format(ErrorMessages.Returns.ReasonFormat, error.Value)),
			IAsyncEnumerable<SharpMail> mailList => await MailPositions(mailbox, mailList)
		};
	}

	/// <summary>Each message's <c>folder:position</c>, which is how <c>@mail</c> names it.</summary>
	private async ValueTask<CallState> MailPositions(SharpPlayer mailbox, IAsyncEnumerable<SharpMail> mailList)
	{
		var results = new List<string>();
		await foreach (var mail in mailList)
		{
			// The message's 1-based position within its folder, which is how @mail names it.
			var position = await Mediator.CreateStream(new GetMailListQuery(mailbox, mail.Folder))
				.Select((m, index) => (m.Id, Position: index + 1))
				.Where(x => x.Id == mail.Id)
				.Select(x => x.Position)
				.FirstOrDefaultAsync();

			if (position > 0)
			{
				results.Add($"{mail.Folder}:{position}");
			}
		}

		return new CallState(string.Join(" ", results));
	}
	[SharpFunction(Name = "mailfrom", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["message"])]
	public async ValueTask<CallState> mailfrom(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// mailfun_fetch's callers all answer a message they could not fetch with #-1 (src/extmail.c).
		if (await FetchMailAsync(parser, executor, args) is not { } mail)
		{
			return new CallState(ErrorMessages.Returns.Nothing);
		}

		var from = await mail.From.WithCancellation(CancellationToken.None);
		// fun_mailfrom writes a bare dbref (src/extmail.c:2198), not an objid.
		return new CallState(from.Object() is { } sender ? $"#{sender.DBRef.Number}" : ErrorMessages.Returns.Nothing);
	}
	/// <summary>
	/// extmail.c:1466 — <c>do_mail_send(executor, args[0], args[1], 0, 1, 0)</c>: the same send
	/// <c>@mail</c> performs, with silent=1 and nosig=0.
	/// </summary>
	[SharpFunction(Name = "mailsend", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["player", "message"])]
	public async ValueTask<CallState> mailsend(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		await SendMail.Handle(parser, LocateService, Mediator, NotifyService,
			new MailDelivery.Services(PermissionService, Mediator, NotifyService, DidItService, AttributeService, ObjectDataService,
			Configuration),
			args["0"].Message!, args["1"].Message!, ["SILENT"]);

		// do_mail_send notifies the sender about a bad recipient, so the function returns nothing.
		return new CallState(string.Empty);
	}
	[SharpFunction(Name = "mailstats", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["player"])]
	public ValueTask<CallState> mailstats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> MailStatsAsync(parser, MailStatsDetail.Counts);

	[SharpFunction(Name = "maildstats", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["player"])]
	public ValueTask<CallState> maildstats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> MailStatsAsync(parser, MailStatsDetail.Status);

	[SharpFunction(Name = "mailfstats", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["player"])]
	public ValueTask<CallState> mailfstats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> MailStatsAsync(parser, MailStatsDetail.Full);

	private enum MailStatsDetail { Counts, Status, Full }

	/// <summary>
	/// PennMUSH's <c>fun_mailstats</c>, which serves all three names. The player is looked up first and
	/// then must be controlled, so anyone may read their own statistics.
	/// </summary>
	private async ValueTask<CallState> MailStatsAsync(IMUSHCodeParser parser, MailStatsDetail detail)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var playerArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		AnySharpObject target;
		if (string.IsNullOrWhiteSpace(playerArg))
		{
			target = executor;
		}
		else
		{
			var locateResult = await LocateService.LocateAndNotifyIfInvalid(
				parser, executor, executor, playerArg, LocateFlags.PlayersPreference);
			if (locateResult is not AnySharpObject located)
			{
				return new CallState(ErrorMessages.Returns.NoSuchPlayer);
			}

			target = located;
		}

		if (target is not SharpPlayer mailbox)
		{
			return new CallState(ErrorMessages.Returns.NoSuchPlayer);
		}

		if (!await PermissionService.Controls(executor, target))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var sent = await TallyMail(Mediator.CreateStream(new GetAllSentMailListQuery(mailbox.Object)));
		var received = await TallyMail(Mediator.CreateStream(new GetAllMailListQuery(mailbox)));

		return new CallState(detail switch
		{
			MailStatsDetail.Counts => $"{sent.Total} {received.Total}",
			MailStatsDetail.Status => $"{sent.Total} {sent.Unread} {sent.Cleared} {received.Total} {received.Unread} {received.Cleared}",
			_ => $"{sent.Total} {sent.Unread} {sent.Cleared} {sent.Bytes} {received.Total} {received.Unread} {received.Cleared} {received.Bytes}"
		});
	}

	[SharpFunction(Name = "mailstatus", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["message"])]
	public async ValueTask<CallState> mailstatus(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// mailfun_fetch's callers all answer a message they could not fetch with #-1 (src/extmail.c).
		if (await FetchMailAsync(parser, executor, args) is not { } mail)
		{
			return new CallState(ErrorMessages.Returns.Nothing);
		}

		// Format status as per @mail/list format: [NCUF+]
		var read = mail.Read ? "-" : "N";
		var cleared = mail.Cleared ? "C" : "-";
		var urgent = mail.Urgent ? "U" : "-";
		var forwarded = mail.Forwarded ? "F" : "-";
		var tagged = mail.Tagged ? "+" : "-";

		return new CallState($"{read}{cleared}{urgent}{forwarded}{tagged}");
	}
	[SharpFunction(Name = "mailsubject", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number"])]
	public async ValueTask<CallState> mailsubject(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// mailfun_fetch's callers all answer a message they could not fetch with #-1 (src/extmail.c).
		if (await FetchMailAsync(parser, executor, args) is not { } mail)
		{
			return new CallState(ErrorMessages.Returns.Nothing);
		}

		return new CallState(mail.Subject);
	}
	[SharpFunction(Name = "mailtime", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["message"])]
	public async ValueTask<CallState> mailtime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// mailfun_fetch's callers all answer a message they could not fetch with #-1 (src/extmail.c).
		if (await FetchMailAsync(parser, executor, args) is not { } mail)
		{
			return new CallState(ErrorMessages.Returns.Nothing);
		}

		// fun_mailtime: show_time(mp->time, 0) (src/extmail.c:2371), the same local time string as time().
		return new CallState(mail.DateSent.ToLocalTime().ToString(PennTimeFormat, System.Globalization.CultureInfo.InvariantCulture));
	}
	/// <summary>fun_malias (<c>src/malias.c</c>): aliases, or one alias's members, with an optional delimiter.</summary>
	[SharpFunction(Name = "malias", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["alias", "delimiter"])]
	public async ValueTask<CallState> malias(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentsOrdered
			.Select(arg => arg.Value.Message?.ToPlainText() ?? string.Empty)
			.ToArray();

		return new CallState(await MailAliases.FunctionAsync(new MailAliases.Services(Mediator, NotifyService, PermissionService),
			executor, args));
	}
}
