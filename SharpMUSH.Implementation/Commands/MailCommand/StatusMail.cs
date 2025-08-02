using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class StatusMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, MString? arg0, MString? arg1, string sw)
	{
		// --> STATUS = tagged, untagged, cleared, uncleared, read, unread, urgent or unurgent

		// What is a good way to handle the 'tag, clear, read, urgent' values in one object call?
		// These are all types of Mail Updates.
		// { bool? tag, bool? clear, bool? read, bool? urgent }

		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var filteredList = await MessageListHelper.Handle(parser, arg0, executor);
		var statusString = arg1?.ToPlainText().ToUpper();
		var id = arg0!.ToPlainText();

		if (filteredList.IsError)
		{
			await parser.NotifyService.Notify(executor, $"#-1 {filteredList.AsError}");
			return MModule.single(filteredList.AsError);
		}

		var actualList = filteredList.AsMailList;

		switch (sw)
		{
			case "UPDATE" when string.IsNullOrEmpty(statusString):
				await parser.NotifyService.Notify(executor, "Update to what?");
				return MModule.single("#-1 Update to what?");
			case "UPDATE"
				when statusString is "CLEAR" or "UNCLEAR" or "TAG" or "UNTAG" or "UNREAD" or "READ" or "URGENT" or "UNURGENT":
				sw = statusString!;
				break;
			case "UPDATE":
				await parser.NotifyService.Notify(executor, $"{statusString} is not a valid status.");
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
			_ => throw new NotImplementedException("Invalid switch somehow made it into StatusMail.Handle")
		};

		foreach (var mail in actualList)
		{
			await parser.Mediator.Send(new UpdateMailCommand(mail, mailUpdate));
			// TODO: Consider how IDs are displayed for Mail on output.
			// This ID isn't useful to anyone. It should be the mail number in their inbox. But what does that mean?
			await parser.NotifyService.Notify(executor, $"Mail {id} updated.");
		}

		return MModule.single(string.Join(" ", actualList.Select(x => x.Id)));
	}
}