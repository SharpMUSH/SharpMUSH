using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Queries.Database;
using System.Globalization;
using DotNext.Threading;

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
	private static (MString Subject, MString Body, bool Given) SplitSubject(MString subjectAndMessage)
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
			? (subject, subjectAndMessage.Substring(position + 1, text.Length - position - 1), true)
			: (subject, subjectAndMessage, false);
	}

	/// <summary>One mailbox a send goes to, under the subject that mailbox sees.</summary>
	/// <param name="Silent">No sender confirmation or refusal for this one.</param>
	public readonly record struct Recipient(SharpPlayer Player, bool Silent, MString Subject);

	/// <summary>extmail.c:1621 — the marker a reply's subject carries, added once.</summary>
	internal const string ReplyPrefix = "Re: ";

	/// <summary><c>send_mail</c> with <c>M_REPLY</c> (<c>extmail.c:1623</c>): <c>Re: </c> once, within the subject's cap.</summary>
	public static MString ReplySubject(MString subject)
	{
		var replied = subject.ToPlainText().StartsWith(ReplyPrefix.TrimEnd(), StringComparison.OrdinalIgnoreCase)
			? subject
			: MarkupText.Concat(MarkupText.Plain(ReplyPrefix), subject);
		return replied.Length > SubjectLength ? replied.Substring(0, SubjectLength) : replied;
	}

	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, ILocateService locateService,
		IMediator mediator, INotifyService notifyService, MailDelivery.Services delivery,
		MString nameList, MString subjectAndMessage, string[] switches, MString? subject = null)
	{
		var urgent = switches.Contains("URGENT");
		var silent = switches.Contains("SILENT");
		var noSignature = switches.Contains("NOSIG");

		var sender = await parser.CurrentState.KnownExecutorObject(mediator);

		// A subject handed over apart (TinyMUX's @mail/quick) is not split out of the message again.
		var (mailSubject, message, subjectGiven) = subject is null
			? SplitSubject(subjectAndMessage)
			: (subject.Length > SubjectLength ? subject.Substring(0, SubjectLength) : subject, subjectAndMessage, true);

		var aliases = new MailAliases.Services(delivery.Mediator, delivery.Notify, delivery.Permissions, delivery.Connections, parser.ServiceProvider.GetRequiredService<IHouseLookService>());
		var recipients = new List<Recipient>();
		var addressed = new List<string>();
		SharpMail? answered = null;
		foreach (var name in ArgHelpers.NameListString(nameList.ToPlainText()))
		{
			// extmail.c:1359 — a number is a message in the current folder, and mails its sender, under its subject
			// marked as a reply unless the send gives one. A number naming no message ends the list there. Only bare
			// digits are a number: a leading + names a mail alias, so +1 is the alias called 1.
			if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
			{
				if (await RepliedMessageAsync(parser, delivery, mediator, notifyService, sender, number) is not Answered original)
				{
					break;
				}

				answered ??= original.Mail;
				recipients.Add(new Recipient(original.Sender, silent,
					subjectGiven ? mailSubject : ReplySubject(original.Mail.Subject)));
				addressed.Add(original.Sender.Object.DBRef.ToString());
				continue;
			}

			var located = await locateService.Locate(parser, sender, sender, name, RecipientMatchFlags);

			// extmail.c:1382 — an unmatched name is reported, not skipped.
			if (located is AnySharpObject and SharpPlayer found)
			{
				recipients.Add(new Recipient(found, silent, mailSubject));
				addressed.Add(found.Object.DBRef.ToString());
				continue;
			}

			// extmail.c:1386 — a name that is no player may be a +alias, mailed to each member.
			if (await MailAliases.RecipientsAsync(aliases, sender, name) is MailAliases.Recipients members)
			{
				recipients.AddRange(members.Members.Select(member => new Recipient(member, silent || members.Silent, mailSubject)));
				addressed.Add(members.Name);
				continue;
			}

			await notifyService.NotifyLocalized(sender, nameof(ErrorMessages.Notifications.MailNoSuchUniquePlayer),
				sender, name);
		}

		var send = answered is null
			? MailDelivery.Threading.NewConversation(addressed)
			: MailDelivery.Threading.Answering(answered, addressed);
		return await DeliverAsync(parser, delivery, sender, recipients, message, signed: !noSignature, urgent, send);
	}

	/// <summary>
	/// Delivers one send to every recipient in turn. Every copy carries <paramref name="send"/>, so they are one
	/// message in one conversation however many mailboxes they reach.
	/// </summary>
	/// <returns>The dbrefs of the mailboxes reached, or why none was.</returns>
	public static async ValueTask<MString> DeliverAsync(IMUSHCodeParser parser, MailDelivery.Services delivery,
		AnySharpObject sender, IReadOnlyList<Recipient> recipients, MString message, bool signed, bool urgent,
		MailDelivery.Threading send)
	{
		var delivered = new List<SharpPlayer>();
		foreach (var (player, quiet, mailSubject) in recipients)
		{
			var letter = new MailDelivery.Letter(mailSubject, message, Signed: signed, urgent, Forwarded: false, send);
			delivered.AddRange(await MailDelivery.SendAsync(parser, delivery, sender, player, letter, quiet));
		}

		// Delivering to nobody has two causes an empty list cannot tell apart, and a non-interactive
		// caller only ever sees this return value — the notifications above go to the sender.
		if (delivered.Count == 0)
		{
			return MarkupText.Plain(recipients.Count == 0
				? ErrorMessages.Returns.NoSuchPlayer
				: ErrorMessages.Returns.RecipientDoesNotAcceptMail);
		}

		return MarkupText.Join(MarkupText.Space, delivered.Select(x => MarkupText.Plain(x.Object.DBRef.ToString())));
	}

	/// <summary>A message being answered, and the player who sent it.</summary>
	public sealed record Answered(SharpMail Mail, SharpPlayer Sender);

	/// <summary>
	/// <c>mail_fetch(player, num)</c> (<c>extmail.c:1366</c>): message <paramref name="number"/> of the current folder,
	/// and its sender. Anonymous mail has no sender to answer, nor has mail whose sender is gone.
	/// </summary>
	private static async ValueTask<Found<Answered>> RepliedMessageAsync(IMUSHCodeParser parser, MailDelivery.Services delivery,
		IMediator mediator, INotifyService notifyService, AnySharpObject sender, int number)
	{
		var original = sender is SharpPlayer player && number > 0
			? await mediator.Send(new GetMailQuery(player, number - 1,
				await MessageListHelper.CurrentMailFolder(parser, delivery.ObjectData, sender)))
			: null;

		return original is null
			? await RefuseReplyAsync(notifyService, sender, "MAIL: You can't reply to nonexistent mail.")
			: await AnsweredAsync(notifyService, sender, original);
	}

	/// <summary>The sender of <paramref name="original"/>, or a refusal told to <paramref name="replier"/>.</summary>
	public static async ValueTask<Found<Answered>> AnsweredAsync(INotifyService notifyService, AnySharpObject replier,
		SharpMail original)
	{
		if (original.Anonymous)
		{
			return await RefuseReplyAsync(notifyService, replier, "MAIL: You can't reply to anonymous mail.");
		}

		return await original.From.WithCancellation(CancellationToken.None) is AnySharpObject and SharpPlayer from
			? new Answered(original, from)
			: await RefuseReplyAsync(notifyService, replier, "MAIL: The sender of that message no longer exists.");
	}

	private static async ValueTask<Found<Answered>> RefuseReplyAsync(INotifyService notifyService, AnySharpObject replier,
		string refusal)
	{
		await notifyService.Notify(replier, refusal, replier);
		return new NotFound();
	}
}
