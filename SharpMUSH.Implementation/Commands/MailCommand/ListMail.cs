using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.MailCommand;

[GenerateOneOf]
public class ErrorOrMailList : OneOfBase<Error<string>, SharpMail[]>
{
	private ErrorOrMailList(OneOf<Error<string>, SharpMail[]> input) : base(input)
	{
	}

	public bool IsError => IsT0;

	public static implicit operator ErrorOrMailList(Error<string> x) => new(x);
	public static implicit operator ErrorOrMailList(SharpMail[] x) => new(x);
}

public static class ListMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, MString? arg0, MString? arg1, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var line = MModule.repeat(MModule.single("-"), 78, MModule.empty());
		var msgList = arg0?.ToPlainText().Trim().ToLower() ?? "folder";
		var folderSplit = msgList.Split(':');
		var rangeSplit = msgList.Split('-');
		IEnumerable<SharpMail> mailList;

		if (folderSplit.Length == 2 && !string.IsNullOrWhiteSpace(folderSplit[0]))
		{
			mailList = await parser.Mediator.Send(new GetMailListQuery(executor.AsPlayer, folderSplit[0]));
			msgList = folderSplit[1];
		}
		else if (msgList == "all")
		{
			mailList = await parser.Mediator.Send(new GetAllMailListQuery(executor.AsPlayer));
		}
		else
		{
			mailList = await parser.Mediator.Send(new GetMailListQuery(executor.AsPlayer, "INBOX"));
		}
		
		ErrorOrMailList filteredList = msgList switch
		{
			_ when msgList.Contains(' ')
				=> new Error<string>("MAIL: Invalid message specification"),
			['*', .. var person] // TODO: Fix this to use a Locate() to find the person.
				=> mailList.Where(x => x.From.Value.Object()?.DBRef == executor.Object().DBRef).ToArray(),
			['~', .. var days] when int.TryParse(days, out var exactDay)
				=> mailList.Where(x => x.DateSent.Date == DateTime.Today.AddDays(-exactDay)).ToArray(),
			['>', .. var days] when int.TryParse(days, out var afterDay)
				=> mailList.Where(x => x.DateSent.Date >= DateTime.Today.AddDays(-afterDay)).ToArray(),
			['<', .. var days] when int.TryParse(days, out var beforeDay)
				=> mailList.Where(x => x.DateSent.Date <= DateTime.Today.AddDays(-beforeDay)).ToArray(),
			"read"
				=> mailList.Where(x => x.Read).ToArray(),
			"unread"
				=> mailList.Where(x => !x.Read).ToArray(),
			"cleared"
				=> mailList.Where(x => x.Cleared).ToArray(),
			"tagged"
				=> mailList.Where(x => x.Tagged).ToArray(),
			"urgent"
				=> mailList.Where(x => x.Urgent).ToArray(),
			"folder"
				=> mailList.ToArray(),
			_ when rangeSplit.Length == 2
			       && int.TryParse(rangeSplit[0], out var left) && int.TryParse(rangeSplit[1], out var right)
				=> mailList.ToArray()[left..right],
			_ when rangeSplit.Length == 2
			       && int.TryParse(rangeSplit[0], out var left) && !int.TryParse(rangeSplit[1], out _)
				=> mailList.ToArray()[left..],
			_ when rangeSplit.Length == 2
			       && !int.TryParse(rangeSplit[0], out _) && int.TryParse(rangeSplit[1], out var right)
				=> mailList.ToArray()[..right],
			_ when int.TryParse(msgList, out var specificMessage)
				=> mailList.Skip(specificMessage).Take(1).ToArray(),
			_ => new Error<string>("MAIL: Invalid message specification")
		};

		if (filteredList.IsError)
		{
			await parser.NotifyService.Notify(executor, filteredList.AsT0.Value);
			return MModule.single(filteredList.AsT0.Value);
		}

		var list = filteredList.AsT1;
		foreach (var folder in list.GroupBy(x => x.Folder))
		{
			var center = MModule.pad(
				markupStr: MModule.single($"  MAIL (folder {folder.Key})  "),
				padStr: MModule.single("-"),
				width: 78,
				MModule.PadType.Center,
				MModule.TruncationType.Truncate);

			MString[] builder =
			[
				center,
				.. folder.Select(DisplayMailLine).ToArray(),
				line
			];
			await parser.NotifyService.Notify(executor, MModule.multipleWithDelimiter(MModule.single("\n"), builder));
		}

		return MModule.empty();
	}

	private static MString DisplayMailLine(SharpMail arg1, int arg2)
	{
		var read = arg1.Read ? "-" : "N";
		var cleared = arg1.Cleared ? "C" : "-";
		var urgent = arg1.Urgent ? "U" : "-";
		var forwarded = arg1.Forwarded ? "F" : "-";
		var tagged = arg1.Tagged ? "+" : "-";
		// TODO: Fix date format. Ex: Mon Sep 18 19:00
		// TODO: PennMUSH adds a * in front of a sender if they are currently online.
		var result =
			$"[{read}{cleared}{urgent}{forwarded}{tagged}]  {arg2,5} {arg1.From.Value.Object()?.Name,14} {arg1.Subject,30} {arg1.DateSent:M/d/yy,16}]";
		return MModule.single(result);
	}
}