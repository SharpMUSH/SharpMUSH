using DotNext.Threading;
using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Commands.Database;
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
	/// PennMUSH resolves a recipient with
	/// <c>match_result(player, current, TYPE_PLAYER, MAT_ME | MAT_ABSOLUTE | MAT_PLAYER)</c>
	/// (extmail.c:1379) before falling back to <c>lookup_player</c>, so <c>me</c>, <c>#dbref</c>,
	/// <c>*name</c> and a bare player name are all recipients. <see cref="LocateFlags.MatchMeForLooker"/>
	/// is what carries "me"; it is not in <c>LocateService</c>'s standard player flag set, which models
	/// <c>lookup_player</c> alone.
	/// </summary>
	private const LocateFlags RecipientMatchFlags =
		LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference | LocateFlags.MatchMeForLooker |
		LocateFlags.AbsoluteMatch | LocateFlags.MatchOptionalWildCardForPlayerName;

	/// <summary>extmail.h:70 — <c>SUBJECT_COOKIE</c>, the character that ends a subject.</summary>
	private const char SubjectCookie = '/';

	/// <summary>extmail.h:71 — <c>SUBJECT_LEN</c>, the cap on a subject's length.</summary>
	private const int SubjectLength = 60;

	/// <summary>
	/// Splits <c>[subject/]message</c> the way extmail.c:1336 does: a doubled cookie is a literal
	/// <c>/</c> that does not end the subject, a single one ends it, and the scan stops at
	/// <see cref="SubjectLength"/>. When no cookie ends the subject PennMUSH rewinds the message
	/// pointer, so the whole thing is the body and the subject is only its opening — which is why
	/// this returns the original string as the body rather than the remainder.
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
				// A doubled cookie contributes one literal character and consumes two.
				if (position + 1 < text.Length && text[position + 1] == SubjectCookie)
				{
					segments.Add(MModule.substring(position, 1, subjectAndMessage));
					position += 2;
					taken++;
					continue;
				}

				break;
			}

			segments.Add(MModule.substring(position, 1, subjectAndMessage));
			position++;
			taken++;
		}

		var subject = segments.Count > 0 ? MModule.concatMany(segments) : MModule.empty();

		// extmail.c:1350 — the subject only counts as given when the scan actually stopped on a
		// cookie, which includes stopping there because the cap ran out.
		return position < text.Length && text[position] == SubjectCookie
			? (subject, MModule.substring(position + 1, text.Length - position - 1, subjectAndMessage))
			: (subject, subjectAndMessage);
	}

	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, IPermissionService permissionService,
		ILocateService locateService, IExpandedObjectDataService objectDataService, IMediator mediator,
		INotifyService notifyService, IAttributeService attributeService,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		MString nameList, MString subjectAndMessage, string[] switches)
	{
		var urgent = switches.Contains("URGENT");
		var silent = switches.Contains("SILENT");
		var noSignature = switches.Contains("NOSIG");

		var sender = await parser.CurrentState.KnownExecutorObject(mediator);

		var knownPlayerList = new List<SharpPlayer>();
		foreach (var name in ArgHelpers.NameListString(nameList.ToPlainText()!))
		{
			var located = await locateService.Locate(parser, sender, sender, name, RecipientMatchFlags);

			// extmail.c:1382 — an unmatched name is reported, not skipped. It used to be filtered out of
			// the recipient list silently, so `@mail me=Subject/Body` sent nothing and said nothing.
			if (located.IsValid() && located.WithoutError().WithoutNone() is { IsPlayer: true } found)
			{
				knownPlayerList.Add(found.AsPlayer);
				continue;
			}

			await notifyService.NotifyLocalized(sender, nameof(ErrorMessages.Notifications.MailNoSuchUniquePlayer),
				sender, name);
		}

		var (subject, message) = SplitSubject(subjectAndMessage);

		if (!noSignature)
		{
			var attribute = mediator.CreateStream(new GetAttributeQuery(sender.Object().DBRef, ["MAILSIGNATURE"]));

			var attributeOpportunity = await attribute.FirstOrDefaultAsync();
			if (attributeOpportunity is not null)
			{
				var attributeValue = attributeOpportunity.Value;
				if (attributeValue.Length > 0)
				{
					message = MModule.concatMany(new[] { message, MModule.single("\n"), attributeValue });
				}
			}
		}

		var mail = new SharpMail
		{
			DateSent = DateTimeOffset.UtcNow,
			Fresh = true,
			Read = false,
			Tagged = false,
			Urgent = urgent,
			Cleared = false,
			Forwarded = false,
			Folder = "INBOX",
			Content = message,
			Subject = subject,
			From = new AsyncLazy<AnyOptionalSharpObject>(async _ =>
			{
				await ValueTask.CompletedTask;
				return sender.WithNoneOption();
			}),
		};

		var delivered = new List<SharpPlayer>();

		foreach (var player in knownPlayerList)
		{
			if (!permissionService.PassesLock(sender, player, LockType.Mail))
			{
				await notifyService.Notify(sender, $"MAIL: {player.Object.Name} does not wish to receive mail from you.", sender);
				continue;
			}

			delivered.Add(player);
			await mediator.Send(new SendMailCommand(sender.Object(), player, mail));

			// real_send_mail (extmail.c:127) gates the sender's confirmation on silent; the delivery
			// notice at :138 sits outside that block and always fires. These were the wrong way round,
			// which is also why mailsend() — PennMUSH calls it with silent=1 — could not tell its
			// recipient anything.
			if (!silent)
			{
				await notifyService.Notify(sender, $"MAIL: You sent a message to {player.Object.Name}.", sender);
			}

			var mailList = mediator.CreateStream(new GetMailListQuery(player, "INBOX"));
			await notifyService.Notify(player,
				$"MAIL: You have received a message ({await mailList.CountAsync()}) from {sender.Object().Name}.", sender);

			if (configuration.CurrentValue.Attribute.AMail)
			{
				var playerAsAny = new AnySharpObject(player);
				var amailAttr = await attributeService.GetAttributeAsync(
					playerAsAny,
					playerAsAny,
					"AMAIL",
					IAttributeService.AttributeMode.Read,
					false);

				if (amailAttr.IsAttribute)
				{
					var attribute = amailAttr.AsAttribute.Last();
					await parser.With(state => state with
					{
						Executor = player.Object.DBRef,
						Enactor = sender.Object().DBRef,
						Caller = state.Executor
					}, async newParser =>
					{
						await newParser.CommandListParse(attribute.Value);
					});
				}
			}
		}

		// Delivering to nobody has two causes, and an empty recipient list cannot tell them apart:
		// either no name resolved, or every name that did resolve refuses mail from this sender. Both
		// are already reported to the sender by notification; naming them in the return value too is
		// what lets a non-interactive caller — the web endpoint — say which one happened.
		if (delivered.Count == 0)
		{
			return MModule.single(knownPlayerList.Count == 0
				? ErrorMessages.Returns.NoSuchPlayer
				: ErrorMessages.Returns.RecipientDoesNotAcceptMail);
		}

		return MModule.multipleWithDelimiter(
			MModule.single(" "),
			delivered
				.Select(x => x.Object.DBRef)
				.Select(x => x.ToString())
				.Select(MModule.single));
	}
}