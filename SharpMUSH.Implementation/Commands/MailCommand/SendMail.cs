using Mediator;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class SendMail
{
	/// <summary>
	/// extmail.c:1379 — <c>MAT_ME | MAT_ABSOLUTE | MAT_PLAYER</c>, then <c>lookup_player</c>.
	/// <see cref="LocateFlags.MatchMeForLooker"/> is named here rather than added to
	/// <c>LocateService</c>'s player flag set, which models <c>lookup_player</c> alone.
	/// </summary>
	private const LocateFlags RecipientMatchFlags =
		LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference | LocateFlags.MatchMeForLooker |
		LocateFlags.AbsoluteMatch | LocateFlags.MatchOptionalWildCardForPlayerName;

	/// <summary>extmail.h:70 — <c>SUBJECT_COOKIE</c>, the character that ends a subject.</summary>
	private const char SubjectCookie = '/';

	/// <summary>extmail.h:71 — <c>SUBJECT_LEN</c>, the cap on a subject's length.</summary>
	private const int SubjectLength = 60;

	/// <summary>
	/// Splits <c>[subject/]message</c> as extmail.c:1336 does: a doubled cookie is a literal, a
	/// single one ends the subject, and the scan stops at <see cref="SubjectLength"/>. With no
	/// cookie PennMUSH rewinds, so the body is the whole string rather than a remainder.
	/// </summary>
	private static (MString Subject, MString Body) SplitSubject(MString subjectAndMessage)
	{
		var text = subjectAndMessage.ToPlainText() ?? string.Empty;
		var segments = new List<MString>();
		var position = 0;
		var taken = 0;

		while (position < text.Length && taken < SubjectLength)
		{
			if (text[position] == SubjectCookie)
			{
				if (position + 1 < text.Length && text[position + 1] == SubjectCookie)
				{
					segments.Add(subjectAndMessage.Substring(position, 1));
					position += 2;
					taken++;
					continue;
				}

				break;
			}

			segments.Add(subjectAndMessage.Substring(position, 1));
			position++;
			taken++;
		}

		var subject = segments.Count > 0 ? MarkupText.Concat(segments) : MarkupText.Empty;

		// extmail.c:1350 — given only if the scan stopped on a cookie, the cap included.
		return position < text.Length && text[position] == SubjectCookie
			? (subject, subjectAndMessage.Substring(position + 1, text.Length - position - 1))
			: (subject, subjectAndMessage);
	}

	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, ILocateService locateService,
		IMediator mediator, INotifyService notifyService, MailDelivery.Services delivery,
		MString nameList, MString subjectAndMessage, string[] switches)
	{
		var urgent = switches.Contains("URGENT");
		var silent = switches.Contains("SILENT");
		var noSignature = switches.Contains("NOSIG");

		var sender = await parser.CurrentState.KnownExecutorObject(mediator);

		var aliases = new MailAliases.Services(delivery.Mediator, delivery.Notify, delivery.Permissions);
		var knownPlayerList = new List<(SharpPlayer Player, bool Silent)>();
		foreach (var name in ArgHelpers.NameListString(nameList.ToPlainText()))
		{
			var located = await locateService.Locate(parser, sender, sender, name, RecipientMatchFlags);

			// extmail.c:1382 — an unmatched name is reported, not skipped.
			if (located is AnySharpObject and SharpPlayer found)
			{
				knownPlayerList.Add((found, silent));
				continue;
			}

			// extmail.c:1386 — a name that is no player may be a +alias, mailed to each member.
			if (await MailAliases.RecipientsAsync(aliases, sender, name) is MailAliases.Recipients recipients)
			{
				knownPlayerList.AddRange(recipients.Members.Select(member => (member, silent || recipients.Silent)));
				continue;
			}

			await notifyService.NotifyLocalized(sender, nameof(ErrorMessages.Notifications.MailNoSuchUniquePlayer),
				sender, name);
		}

		var (subject, message) = SplitSubject(subjectAndMessage);

		var signature = MarkupText.Empty;
		if (!noSignature
				&& await mediator.CreateStream(new GetAttributeQuery(sender.Object().DBRef, ["MAILSIGNATURE"]))
					.FirstOrDefaultAsync() is { } signatureAttribute)
		{
			signature = signatureAttribute.Value;
		}

		var letter = new MailDelivery.Letter(subject, message, signature, urgent, Forwarded: false);

		var delivered = new List<SharpPlayer>();
		foreach (var (player, quiet) in knownPlayerList)
		{
			delivered.AddRange(await MailDelivery.SendAsync(parser, delivery, sender, player, letter, quiet));
		}

		// Delivering to nobody has two causes an empty list cannot tell apart, and a non-interactive
		// caller only ever sees this return value — the notifications above go to the sender.
		if (delivered.Count == 0)
		{
			return MarkupText.Plain(knownPlayerList.Count == 0
				? ErrorMessages.Returns.NoSuchPlayer
				: ErrorMessages.Returns.RecipientDoesNotAcceptMail);
		}

		return MarkupText.Join(MarkupText.Space, delivered.Select(x => MarkupText.Plain(x.Object.DBRef.ToString())));
	}
}
