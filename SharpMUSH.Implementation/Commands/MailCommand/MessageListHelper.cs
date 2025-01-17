using OneOf;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
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
	public string AsError => AsT0.Value;
	public SharpMail[] AsMailList => AsT1;
	
	public static implicit operator ErrorOrMailList(Error<string> x) => new(x);
	public static implicit operator ErrorOrMailList(SharpMail[] x) => new(x);
}

public static class MessageListHelper
{
	public static async ValueTask<string> CurrentMailFolder(IMUSHCodeParser parser, AnySharpObject executor)
	{
		var mailData = await parser.ObjectDataService.GetExpandedDataAsync<ExpandedMailData>(executor.Object(), typeof(ExpandedMailData));
		
		if (mailData == null)
		{
			mailData = new ExpandedMailData();
			await parser.ObjectDataService.SetExpandedDataAsync(executor.Object(), typeof(ExpandedMailData), mailData);
		}		
		
		return mailData.ActiveFolder;
	}
	
	public static async ValueTask<ErrorOrMailList>  Handle(IMUSHCodeParser parser, MString? arg0, AnySharpObject executor)
	{
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
			var currentFolder = await MessageListHelper.CurrentMailFolder(parser, executor);
			mailList = await parser.Mediator.Send(new GetMailListQuery(executor.AsPlayer, currentFolder));
		}

		ErrorOrMailList filteredList = msgList switch
		{
			_ when msgList.Contains(' ')
				=> new Error<string>("MAIL: Invalid message specification"),
			['*', .. var person] // TODO: Fix this to use a Locate() to find the person.
				=> mailList.Where(x => x.From.Value.Object()?.Name.StartsWith(person) ?? false).ToArray(),
			['~', .. var days] when int.TryParse(days, out var exactDay)
				=> mailList.Where(x => x.DateSent >= DateTimeOffset.UtcNow.AddDays(-exactDay-1) 
				                       && x.DateSent <=  DateTimeOffset.UtcNow.AddDays(-exactDay)).ToArray(),
			['>', .. var days] when int.TryParse(days, out var afterDay)
				=> mailList.Where(x => x.DateSent >= DateTimeOffset.UtcNow.AddDays(-afterDay)).ToArray(),
			['<', .. var days] when int.TryParse(days, out var beforeDay)
				=> mailList.Where(x => x.DateSent <= DateTimeOffset.UtcNow.AddDays(-beforeDay)).ToArray(),
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
				=> mailList.Skip(left - 1).Take(right - left).ToArray(),
			_ when rangeSplit.Length == 2
			       && int.TryParse(rangeSplit[0], out var left) && !int.TryParse(rangeSplit[1], out _)
				=> mailList.Skip(left - 1).ToArray(),
			_ when rangeSplit.Length == 2
			       && !int.TryParse(rangeSplit[0], out _) && int.TryParse(rangeSplit[1], out var right)
				=> mailList.Take(right).ToArray(),
			_ when int.TryParse(msgList, out var specificMessage)
				=> mailList.Skip(specificMessage).Take(1).ToArray(),
			[] when msgList.Length == 0
				=> mailList.ToArray(),
			_ => new Error<string>("MAIL: Invalid message specification")
		};
		
		return filteredList;
	}
}