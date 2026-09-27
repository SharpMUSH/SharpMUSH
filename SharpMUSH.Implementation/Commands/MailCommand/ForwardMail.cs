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

	public static async ValueTask<MString> Handle(IMUSHCodeParser parser,
		IExpandedObjectDataService objectDataService,
		ILocateService locateService,
		IMediator mediator,
		INotifyService notifyService,
		MailDelivery.Services delivery,
		int mailNumber, string target)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		if (executor is not SharpPlayer executorPlayer)
		{
			throw new InvalidOperationException("@mail reads a player's own mail, and its dispatcher routes only players here.");
		}

		// extmail.c:1261 — each name in the list is matched on its own; an unmatched one is reported.
		var recipients = new List<SharpPlayer>();
		foreach (var name in ArgHelpers.NameListString(target))
		{
			if (await locateService.Locate(parser, executor, executor, name, RecipientMatchFlags)
					is AnySharpObject and SharpPlayer found)
			{
				recipients.Add(found);
				continue;
			}

			await notifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MailNoSuchUniquePlayer),
				executor, name);
		}

		var currentFolder = await MessageListHelper.CurrentMailFolder(parser, objectDataService, executor);

		return await ForwardAsync(parser, mediator, notifyService, delivery, executorPlayer, recipients, mailNumber,
			currentFolder);
	}

	private static async ValueTask<MString> ForwardAsync(IMUSHCodeParser parser, IMediator mediator,
		INotifyService notifyService, MailDelivery.Services delivery, SharpPlayer executor,
		IReadOnlyList<SharpPlayer> recipients, int mailNumber, string currentFolder)
	{
		// Message numbers are 1-based, as for @mail/read; the store indexes from 0.
		var mail = await mediator.Send(new GetMailQuery(executor, mailNumber - 1, currentFolder));

		if (mail is null)
		{
			return MarkupText.Plain(ErrorMessages.Returns.MailNotFound);
		}

		var subject = mail.Subject.ToPlainText().StartsWith(ForwardPrefix.TrimEnd(), StringComparison.OrdinalIgnoreCase)
			? mail.Subject
			: MarkupText.Concat(MarkupText.Plain(ForwardPrefix), mail.Subject);

		// do_mail_fwd sends with silent=1 (extmail.c:1296) and then only counts attempts, so a refused
		// forward reads as a success. Refusals are reported here instead.
		var delivered = new List<SharpPlayer>();
		foreach (var recipient in recipients)
		{
			delivered.AddRange(await MailDelivery.SendAsync(parser, delivery, executor, recipient,
				new MailDelivery.Letter(subject, mail.Content, MarkupText.Empty, Urgent: false, Forwarded: true),
				silent: false));
		}

		await notifyService.Notify(executor, $"MAIL: {delivered.Count} messages forwarded.", executor);

		// As for @mail: a caller that sees only this return value can tell no match from a refusal.
		if (delivered.Count == 0)
		{
			return MarkupText.Plain(recipients.Count == 0
				? ErrorMessages.Returns.NoSuchPlayer
				: ErrorMessages.Returns.RecipientDoesNotAcceptMail);
		}

		return MarkupText.Plain(string.Join(' ', delivered.Select(player => player.Object.DBRef.ToString())));
	}
}
