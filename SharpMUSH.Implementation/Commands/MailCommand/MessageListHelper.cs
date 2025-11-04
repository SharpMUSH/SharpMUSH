using Mediator;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

[GenerateOneOf]
public class ErrorOrMailList : OneOfBase<Error<string>, IAsyncEnumerable<SharpMail>>
{
	private ErrorOrMailList(OneOf<Error<string>, IAsyncEnumerable<SharpMail>> input) : base(input)
	{
	}

	public bool IsError => IsT0;
	public string AsError => AsT0.Value;
	public IAsyncEnumerable<SharpMail> AsMailList => AsT1;

	public static implicit operator ErrorOrMailList(Error<string> x) => new(x);

	public static ErrorOrMailList FromAsyncEnumerable(IAsyncEnumerable<SharpMail> x) 
		=> new(OneOf<Error<string>, IAsyncEnumerable<SharpMail>>.FromT1(x));
}

public static class MessageListHelper
{
	public static async ValueTask<string> CurrentMailFolder(IMUSHCodeParser parser, IExpandedObjectDataService objectDataService, AnySharpObject executor)
	{
		var mailData = await objectDataService.GetExpandedDataAsync<ExpandedMailData>(executor.Object());

		if (mailData?.ActiveFolder != null)
		{
			return mailData.ActiveFolder!;
		}

		mailData = new ExpandedMailData(Folders: ["INBOX"], ActiveFolder: "INBOX");
		await objectDataService.SetExpandedDataAsync(mailData, executor.Object());

		return mailData.ActiveFolder!;
	}

	public static async ValueTask<ErrorOrMailList> Handle(IMUSHCodeParser parser, IExpandedObjectDataService objectDataService,IMediator? mediator, INotifyService? notifyService,  MString? arg0, AnySharpObject executor)
	{
		var msgList = arg0?.ToPlainText().Trim().ToLower() ?? "folder";
		var folderSplit = msgList.Split(':');
		var rangeSplit = msgList.Split('-');
		IAsyncEnumerable<SharpMail> mailList;

		if (folderSplit.Length == 2 && !string.IsNullOrWhiteSpace(folderSplit[0]))
		{
			mailList = mediator!.CreateStream(new GetMailListQuery(executor.AsPlayer, folderSplit[0]));
			msgList = folderSplit[1];
		}
		else if (msgList == "all")
		{
			mailList = mediator!.CreateStream(new GetAllMailListQuery(executor.AsPlayer));
		}
		else
		{
			var currentFolder = await CurrentMailFolder(parser, objectDataService, executor);
			mailList = mediator!.CreateStream(new GetMailListQuery(executor.AsPlayer, currentFolder));
		}

		ErrorOrMailList filteredList = msgList switch
		{
			_ when msgList.Contains(' ')
				=> new Error<string>("MAIL: Invalid message specification"),
			['*', .. var person] // TODO: Fix this to use a Locate() to find the person.
				=> ErrorOrMailList.FromAsyncEnumerable(mailList
					.Where(async (x,_) =>
						(await x.From.WithCancellation(CancellationToken.None))
						.Object()?.Name.StartsWith(person) ?? false)),
			['~', .. var days] when int.TryParse(days, out var exactDay)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.DateSent >= DateTimeOffset.UtcNow.AddDays(-exactDay - 1)
				                                                           && x.DateSent <= DateTimeOffset.UtcNow.AddDays(-exactDay))),
			['>', .. var days] when int.TryParse(days, out var afterDay)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.DateSent >= DateTimeOffset.UtcNow.AddDays(-afterDay))),
			['<', .. var days] when int.TryParse(days, out var beforeDay)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.DateSent <= DateTimeOffset.UtcNow.AddDays(-beforeDay))),
			"read"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.Read)),
			"unread"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => !x.Read)),
			"cleared"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.Cleared)),
			"tagged"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.Tagged)),
			"urgent"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.Urgent)),
			"folder"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList),
			_ when rangeSplit.Length == 2
			       && int.TryParse(rangeSplit[0], out var left) && int.TryParse(rangeSplit[1], out var right)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Skip(left - 1).Take(right - left)),
			_ when rangeSplit.Length == 2
			       && int.TryParse(rangeSplit[0], out var left) && !int.TryParse(rangeSplit[1], out _)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Skip(left - 1)),
			_ when rangeSplit.Length == 2
			       && !int.TryParse(rangeSplit[0], out _) && int.TryParse(rangeSplit[1], out var right)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Take(right)),
			_ when int.TryParse(msgList, out var specificMessage)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Skip(specificMessage).Take(1)),
			[] when msgList.Length == 0
				=> ErrorOrMailList.FromAsyncEnumerable(mailList),
			_ => new Error<string>("MAIL: Invalid message specification")
		};

		return filteredList;
	}
	
	public static async ValueTask<ErrorOrMailList> HandleSent(IMUSHCodeParser parser,IMediator? mediator, INotifyService? notifyService,  MString? arg0, AnySharpObject executor, SharpPlayer target)
	{
		await ValueTask.CompletedTask;
		var msgList = arg0?.ToPlainText().Trim().ToLower() ?? "folder";
		var folderSplit = msgList.Split(':');
		var rangeSplit = msgList.Split('-');
		IAsyncEnumerable<SharpMail> mailList;

		if (folderSplit.Length == 2 && !string.IsNullOrWhiteSpace(folderSplit[0]))
		{
			mailList = mediator!.CreateStream(new GetSentMailListQuery(executor.Object(), target));
			msgList = folderSplit[1];
		}
		else if (msgList == "all")
		{
			mailList = mediator!.CreateStream(new GetAllSentMailListQuery(executor.Object()));
		}
		else
		{
			mailList = mediator!.CreateStream(new GetSentMailListQuery(executor.Object(), target));
		}

		ErrorOrMailList filteredList = msgList switch
		{
			_ when msgList.Contains(' ')
				=> new Error<string>("MAIL: Invalid message specification"),
			['~', .. var days] when int.TryParse(days, out var exactDay)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.DateSent >= DateTimeOffset.UtcNow.AddDays(-exactDay - 1)
				                                                           && x.DateSent <= DateTimeOffset.UtcNow.AddDays(-exactDay))),
			['>', .. var days] when int.TryParse(days, out var afterDay)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.DateSent >= DateTimeOffset.UtcNow.AddDays(-afterDay))),
			['<', .. var days] when int.TryParse(days, out var beforeDay)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.DateSent <= DateTimeOffset.UtcNow.AddDays(-beforeDay))),
			"read"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.Read)),
			"unread"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => !x.Read)),
			"cleared"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.Cleared)),
			"tagged"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.Tagged)),
			"urgent"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.Urgent)),
			_ when rangeSplit.Length == 2
			       && int.TryParse(rangeSplit[0], out var left) && int.TryParse(rangeSplit[1], out var right)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Skip(left - 1).Take(right - left)),
			_ when rangeSplit.Length == 2
			       && int.TryParse(rangeSplit[0], out var left) && !int.TryParse(rangeSplit[1], out _)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Skip(left - 1)),
			_ when rangeSplit.Length == 2
			       && !int.TryParse(rangeSplit[0], out _) && int.TryParse(rangeSplit[1], out var right)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Take(right)),
			_ when int.TryParse(msgList, out var specificMessage)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Skip(specificMessage).Take(1)),
			[] when msgList.Length == 0
				=> ErrorOrMailList.FromAsyncEnumerable(mailList),
			_ => new Error<string>("MAIL: Invalid message specification")
		};

		return filteredList;
	}
}