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

		// extmail.c:1350 — given only if the scan stopped on a cookie, the cap included.
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

			// extmail.c:1382 — an unmatched name is reported, not skipped.
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

			// real_send_mail gates the sender's confirmation on silent (extmail.c:127); the delivery
			// notice at :138 sits outside that block and always fires.
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

		// Delivering to nobody has two causes an empty list cannot tell apart, and a non-interactive
		// caller only ever sees this return value — the notifications above go to the sender.
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