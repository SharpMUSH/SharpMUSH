using System.ComponentModel;
using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class StatusMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, 
		IExpandedObjectDataService objectDataService, 
		IMediator mediator, 
		INotifyService notifyService, 
		MString? arg0, MString? arg1, string sw)
	{
		// --> STATUS = tagged, untagged, cleared, uncleared, read, unread, urgent or unurgent

		// What is a good way to handle the 'tag, clear, read, urgent' values in one object call?
		// These are all types of Mail Updates.
		// { bool? tag, bool? clear, bool? read, bool? urgent }

		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var filteredList = await MessageListHelper.Handle(parser, objectDataService, mediator, notifyService, arg0, executor);
		var statusString = arg1?.ToPlainText().ToUpper();
		var id = arg0!.ToPlainText();

		if (filteredList.IsError)
		{
			await notifyService.Notify(executor, $"#-1 {filteredList.AsError}");
			return MModule.single(filteredList.AsError);
		}

		var actualList = filteredList.AsMailList;

		switch (sw)
		{
			case "UPDATE" when string.IsNullOrEmpty(statusString):
				await notifyService.Notify(executor, "Update to what?");
				return MModule.single("#-1 Update to what?");
			case "UPDATE"
				when statusString is "CLEAR" or "UNCLEAR" or "TAG" or "UNTAG" or "UNREAD" or "READ" or "URGENT" or "UNURGENT":
				sw = statusString;
				break;
			case "UPDATE":
				await notifyService.Notify(executor, $"{statusString} is not a valid status.");
				return MModule.single($"{statusString} is not a valid status.");
		}

		var mailUpdate = sw switch
		{
			"CLEAR" => MailUpdate.ClearEdit(true),
			"UNCLEAR" => MailUpdate.ClearEdit(false),
			"TAG" => MailUpdate.TaggedEdit(true),
			"UNTAG" => MailUpdate.TaggedEdit(false),
			"UNREAD" => MailUpdate.ReadEdit(false),
			"READ" => MailUpdate.ReadEdit(true),
			"URGENT" => MailUpdate.UrgentEdit(true),
			"UNURGENT" => MailUpdate.UrgentEdit(false),
			_ => throw new ArgumentOutOfRangeException(nameof(sw), "Invalid switch somehow made it into StatusMail.Handle")
		};

		List<string> idList = [];
		var index = 1;
		await foreach (var mail in actualList)
		{
			await mediator.Send(new UpdateMailCommand(mail, mailUpdate));
			// Use per-player inbox numbers (1-based index) instead of database IDs
			// The index is calculated in-memory based on mail position in the filtered list
			await notifyService.Notify(executor, $"Mail {index} updated.");
			idList.Add(index.ToString());
			index++;
		}

		return MModule.single(string.Join(" ", idList));
	}
}