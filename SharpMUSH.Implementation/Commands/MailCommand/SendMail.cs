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

		var subjectBodySplit = MModule.indexOf(subjectAndMessage, "/");

		var subject = subjectBodySplit > -1
			? MModule.substring(0, subjectBodySplit, subjectAndMessage)
			: MModule.substring(0, Math.Min(20, subjectAndMessage.Length), subjectAndMessage);

		var message = subjectBodySplit > -1
			? MModule.substring(subjectBodySplit + 1, subjectAndMessage.Length - subjectBodySplit, subjectAndMessage)
			: subjectAndMessage;

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
			await notifyService.Notify(sender, $"MAIL: You sent a message to {player.Object.Name}.", sender);

			if (!silent)
			{
				var mailList = mediator.CreateStream(new GetMailListQuery(player, "INBOX"));
				await notifyService.Notify(player,
					$"MAIL: You have received a message ({await mailList.CountAsync()}) from {sender.Object().Name}.", sender);
			}

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

		return MModule.multipleWithDelimiter(
			MModule.single(" "),
			delivered
				.Select(x => x.Object.DBRef)
				.Select(x => x.ToString())
				.Select(MModule.single));
	}
}