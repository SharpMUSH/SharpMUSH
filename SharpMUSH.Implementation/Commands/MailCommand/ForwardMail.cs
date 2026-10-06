using Mediator;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class ForwardMail
{
	/// <summary>extmail.c:1621 — the marker a forward's subject carries, added once.</summary>
	private const string ForwardPrefix = "Fwd: ";

	/// <summary>
	/// extmail.c:1283 — <c>match_result(..., TYPE_PLAYER, MAT_ME | MAT_ABSOLUTE | MAT_PMATCH | MAT_TYPE)</c>:
	/// a recipient is a player by name anywhere, not an object nearby.
	/// </summary>
	private const LocateFlags RecipientMatchFlags =
		LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference | LocateFlags.MatchMeForLooker |
		LocateFlags.AbsoluteMatch | LocateFlags.MatchOptionalWildCardForPlayerName;

	/// <summary>
	/// <c>do_mail_fwd</c> (<c>extmail.c:1219</c>): every message <paramref name="msgList"/> matches, by the same
	/// <c>parse_msglist</c> grammar the other switches take, is forwarded to each entry of
	/// <paramref name="targets"/> in turn.
	/// </summary>
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser,
		IExpandedObjectDataService objectDataService,
		ILocateService locateService,
		IMediator mediator,
		INotifyService notifyService,
		MailDelivery.Services delivery,
		MString? msgList, string? targets)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		if (executor is not SharpPlayer executorPlayer)
		{
			throw new InvalidOperationException("@mail reads a player's own mail, and its dispatcher routes only players here.");
		}

		// extmail.c:1235 — the message list is parsed (and refused) before the recipients are looked at.
		return await MessageListHelper.Handle(parser, objectDataService, mediator, notifyService, msgList, executor) switch
		{
			IAsyncEnumerable<SharpMail> messages => await ForwardListAsync(parser, objectDataService, locateService,
				mediator, notifyService, delivery, executorPlayer, messages, targets),
			Error<string> error => await MessageListHelper.RefuseAsync(notifyService, executor, error.Value)
		};
	}

	private static async ValueTask<MString> ForwardListAsync(IMUSHCodeParser parser,
		IExpandedObjectDataService objectDataService, ILocateService locateService, IMediator mediator,
		INotifyService notifyService, MailDelivery.Services delivery, SharpPlayer executor,
		IAsyncEnumerable<SharpMail> messageList, string? targets)
	{
		if (string.IsNullOrWhiteSpace(targets))
		{
			// extmail.c:1239
			await notifyService.Notify(executor, "MAIL: To whom should I forward?", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchPlayer);
		}

		if (!await mediator.CreateStream(new GetAllMailListQuery(executor)).AnyAsync())
		{
			// extmail.c:1249
			await notifyService.Notify(executor, "MAIL: You have no messages to forward.", executor);
			return MarkupText.Plain(ErrorMessages.Returns.MailNotFound);
		}

		// extmail.c:1252 marks the last message before forwarding so a forward back to oneself is not forwarded
		// again; reading the list first does the same.
		var messages = await messageList.ToListAsync();
		var currentFolder = await MessageListHelper.CurrentMailFolder(parser, objectDataService, executor);
		var names = ArgHelpers.NameListString(targets).ToList();

		var attempts = 0;
		var delivered = new List<SharpPlayer>();
		foreach (var mail in messages)
		{
			// extmail.c:1261 — each entry is matched afresh for each message, so an unmatched one is reported
			// once per message.
			foreach (var name in names)
			{
				if (await RecipientAsync(parser, locateService, mediator, notifyService, executor, name, currentFolder)
						is not AnySharpObject recipient)
				{
					continue;
				}

				attempts++;
				delivered.AddRange(await ForwardAsync(parser, delivery, executor, recipient, mail));
			}
		}

		await notifyService.Notify(executor, $"MAIL: {attempts} messages forwarded.", executor);

		// As for @mail: a caller that sees only this return value can tell no match from a refusal.
		if (delivered.Count == 0)
		{
			return MarkupText.Plain(messages.Count == 0
				? ErrorMessages.Returns.MailNotFound
				: attempts == 0
					? ErrorMessages.Returns.NoSuchPlayer
					: ErrorMessages.Returns.RecipientDoesNotAcceptMail);
		}

		return MarkupText.Plain(string.Join(' ', delivered.Select(player => player.Object.DBRef.ToString())));
	}

	/// <summary>
	/// One entry of the recipient list (<c>extmail.c:1265</c>): a number (by <c>atoi</c>) forwards to the sender
	/// of that message in the current folder; anything else is a player's name.
	/// </summary>
	private static async ValueTask<AnyOptionalSharpObject> RecipientAsync(IMUSHCodeParser parser,
		ILocateService locateService, IMediator mediator, INotifyService notifyService, SharpPlayer executor,
		string name, string currentFolder)
	{
		var number = LeadingInteger(name);
		if (number != 0)
		{
			var replyTo = number > 0 ? await mediator.Send(new GetMailQuery(executor, number - 1, currentFolder)) : null;
			if (replyTo is null)
			{
				await notifyService.Notify(executor, "MAIL: You can't reply to nonexistant mail.", executor);
				return new AnyOptionalSharpObject(new None());
			}

			return await replyTo.From.WithCancellation(CancellationToken.None);
		}

		if (await locateService.Locate(parser, executor, executor, name, RecipientMatchFlags)
				is AnySharpObject and SharpPlayer found)
		{
			return found;
		}

		await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MailNoSuchUniquePlayer),
			executor, name);
		return new AnyOptionalSharpObject(new None());
	}

	/// <summary>C's <c>atoi</c>: optional leading space and sign, then digits; 0 when there are none.</summary>
	private static int LeadingInteger(string text)
	{
		var span = text.AsSpan().TrimStart();
		var length = span is ['+' or '-', ..] ? 1 : 0;
		while (length < span.Length && char.IsAsciiDigit(span[length]))
		{
			length++;
		}

		return int.TryParse(span[..length], out var value) ? value : 0;
	}

	private static async ValueTask<SharpPlayer[]> ForwardAsync(IMUSHCodeParser parser,
		MailDelivery.Services delivery, SharpPlayer executor, AnySharpObject recipient, SharpMail mail)
	{
		// real_send_mail (extmail.c:1566): a forward to something that is not a player is dropped silently.
		if (recipient is not SharpPlayer player)
		{
			return [];
		}

		var subject = mail.Subject.ToPlainText().StartsWith(ForwardPrefix.TrimEnd(), StringComparison.OrdinalIgnoreCase)
			? mail.Subject
			: MarkupText.Concat(MarkupText.Plain(ForwardPrefix), mail.Subject);

		// extmail.c:1292 — do_mail_fwd sends with silent=1, so no "You sent your message to" line per
		// recipient, and its count is of attempts: a refused forward still counts.
		return await MailDelivery.SendAsync(parser, delivery, executor, player,
			new MailDelivery.Letter(subject, mail.Content, Signed: false, Urgent: false, Forwarded: true),
			silent: true);
	}
}
