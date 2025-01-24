using DotNext.Threading;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class SendMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, MString nameList, MString subjectAndMessage, string[] switches)
	{
		var urgent = switches.Contains("URGENT");
		var silent = switches.Contains("SILENT");
		var noSignature = switches.Contains("NOSIG");
		
		var sender = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		
		var playerList = await Functions.Functions.PopulatedNameList(parser, nameList.ToPlainText()!);
		var knownPlayerList = playerList.Where(x => x != null).Select(x => x!).ToList();
		var subjectBodySplit = MModule.indexOf(subjectAndMessage, MModule.single("/"));
		
		var subject = subjectBodySplit > -1 
			? MModule.substring(0, subjectBodySplit, subjectAndMessage) 
			: MModule.substring(0, Math.Min(20, subjectAndMessage.Length), subjectAndMessage);
		
		var message = subjectBodySplit > -1
			? MModule.substring(subjectBodySplit + 1, subjectAndMessage.Length - subjectBodySplit, subjectAndMessage) 
			: subjectAndMessage;
		
		if (!noSignature)
		{
			var attribute = await parser.Mediator.Send(new GetAttributeQuery(sender.Object().DBRef, ["MAILSIGNATURE"]));
			var attributeValue = attribute?.FirstOrDefault()?.Value;
			if (attributeValue != null)
			{
				MModule.concat(message, MModule.single("\n"), attributeValue);
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
			if (!parser.PermissionService.PassesLock(sender, player, LockType.Mail))
			{
				await parser.NotifyService.Notify(sender, $"MAIL: {player.Object.Name} does not wish to receive mail from you.");
				continue;
			}
				
			await parser.Mediator.Send(new SendMailCommand(sender.Object(), player, mail));
			await parser.NotifyService.Notify(sender, $"MAIL: You sent a message to {player.Object.Name}.");

			if (!silent)
			{
				var mailList = await parser.Mediator.Send(new GetMailListQuery(player, "INBOX"));
				await parser.NotifyService.Notify(player, $"MAIL: You have received a message ({mailList.Count()}) from {sender.Object().Name}.");
			}
			
			// TODO: If AMAIL is config true, and AMAIL &attribute is set on the target, trigger it.
		}

		return MModule.multipleWithDelimiter(
			MModule.single(" "), 
			knownPlayerList
				.Select(x => x.Object.DBRef)
				.Select(x => x.ToString())
				.Select(MModule.single));
	}
}