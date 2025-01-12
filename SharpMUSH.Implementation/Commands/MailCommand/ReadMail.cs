using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class ReadMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, int messageNumber, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var line = MModule.repeat(MModule.single("-"), 78, MModule.empty());

		var actualMail = await parser.Mediator.Send(new GetMailQuery(executor.AsPlayer, messageNumber, "INBOX"));

		if (actualMail is null)
		{
			await parser.NotifyService.Notify(executor, $"MAIL: You do not have a mail with number: {messageNumber}");
		}
		
		// TODO: Mark mail as Read (and no longer fresh)

		var dateline = MModule.pad(
			MModule.single(actualMail!.DateSent.ToString("ddd MMM dd HH:mm yyyy")),
			MModule.single(" "),
			25,
			MModule.PadType.Right,
			MModule.TruncationType.Truncate);

		var messageBuilder = new List<MString>
		{
			line,   
			MModule.single($"From: {actualMail.From.Value.Object()!.Name}"),
			MModule.single($"Date: {dateline,-20} Folder: {actualMail.Folder,-20} Message: {messageNumber,5}"),
			MModule.single($"Status: {(actualMail.Read ? "Read" : "Unread")}"),
			MModule.concat(MModule.single("Subject: "), actualMail.Subject),
			line,
			actualMail.Content, // BUG: discovered bug - ansi is bleeding.
			line
		};

		var output = MModule.multipleWithDelimiter(MModule.single("\n"), messageBuilder);
		await parser.NotifyService.Notify(executor, output);

		return output;
	}
}