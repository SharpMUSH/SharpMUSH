using DotNext.Threading;
using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class SendMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, IPermissionService permissionService,
		IExpandedObjectDataService objectDataService, IMediator mediator, INotifyService notifyService,
		IAttributeService attributeService, IOptionsWrapper<SharpMUSHOptions> configuration,
		MString nameList, MString subjectAndMessage, string[] switches)
	{
		var urgent = switches.Contains("URGENT");
		var silent = switches.Contains("SILENT");
		var noSignature = switches.Contains("NOSIG");

		var sender = await parser.CurrentState.KnownExecutorObject(mediator);

		var playerList = ArgHelpers.PopulatedNameList(mediator, nameList.ToPlainText()!);
		var knownPlayerList = await playerList.Where(x => x != null).Select(x => x!).ToListAsync();
		var subjectBodySplit = MModule.indexOf(subjectAndMessage, MModule.single("/"));

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
					MModule.concat(message, MModule.single("\n"), attributeValue);
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
			Folder = "INBOX", // All mail goes to the INBOX!
			Content = message,
			Subject = subject,
			From = new AsyncLazy<AnyOptionalSharpObject>(async _ =>
			{
				await ValueTask.CompletedTask;
				return sender.WithNoneOption();
			}),
		};

		foreach (var player in knownPlayerList)
		{
			if (!permissionService.PassesLock(sender, player, LockType.Mail))
			{
				await notifyService.Notify(sender, $"MAIL: {player.Object.Name} does not wish to receive mail from you.");
				continue;
			}

			await mediator.Send(new SendMailCommand(sender.Object(), player, mail));
			await notifyService.Notify(sender, $"MAIL: You sent a message to {player.Object.Name}.");

			if (!silent)
			{
				var mailList = mediator.CreateStream(new GetMailListQuery(player, "INBOX"));
				await notifyService.Notify(player,
					$"MAIL: You have received a message ({await mailList.CountAsync()}) from {sender.Object().Name}.");
			}

			// Trigger AMAIL attribute if configured and present
			if (configuration.CurrentValue.Attribute.AMail)
			{
				var playerAsAny = new AnySharpObject(player);
				var amailAttr = await attributeService.GetAttributeAsync(
					playerAsAny,  // executor is the player themselves
					playerAsAny,  // object is also the player
					"AMAIL",
					IAttributeService.AttributeMode.Read,
					false);

				if (amailAttr.IsAttribute)
				{
					var attribute = amailAttr.AsAttribute.Last();
					// Execute AMAIL attribute with player as executor and sender as enactor
					await parser.With(state => state with
					{
						Executor = player.Object.DBRef,
						Enactor = sender.Object().DBRef
					}, async newParser =>
					{
						await newParser.CommandListParse(attribute.Value);
					});
				}
			}
		}

		return MModule.multipleWithDelimiter(
			MModule.single(" "),
			knownPlayerList
				.Select(x => x.Object.DBRef)
				.Select(x => x.ToString())
				.Select(MModule.single));
	}
}