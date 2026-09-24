using Mediator;
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

		var maybeLocate = await locateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, target,
			LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference);
		var currentFolder = await MessageListHelper.CurrentMailFolder(parser, objectDataService, executor);

		return maybeLocate switch
		{
			AnySharpObject and SharpPlayer targetPlayer => await ForwardAsync(parser, mediator, notifyService, delivery,
				executorPlayer, targetPlayer, mailNumber, currentFolder),
			AnySharpObject => MarkupText.Plain("MAIL: Cannot forward to non-player."),
			Error<CallState> error => error.Value.Message!
		};
	}

	private static async ValueTask<MString> ForwardAsync(IMUSHCodeParser parser, IMediator mediator,
		INotifyService notifyService, MailDelivery.Services delivery, SharpPlayer executor, SharpPlayer targetPlayer,
		int mailNumber, string currentFolder)
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
		var delivered = await MailDelivery.SendAsync(parser, delivery, executor, targetPlayer,
			new MailDelivery.Letter(subject, mail.Content, MarkupText.Empty, Urgent: false, Forwarded: true), silent: false);

		await notifyService.Notify(executor, $"MAIL: {delivered.Length} messages forwarded.", executor);

		return delivered.Length == 0
			? MarkupText.Plain(ErrorMessages.Returns.RecipientDoesNotAcceptMail)
			: MarkupText.Plain(targetPlayer.Object.DBRef.ToString());
	}
}
