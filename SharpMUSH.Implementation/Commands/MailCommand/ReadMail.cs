using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class ReadMail
{
	/*
	 *-----------------------------------------------------------------------------
		From: Liminality                                               (Conn)
		Date: Sat Jan 11 03:38:31 2025    Folder:  0   Message: 145
		Status: Unread
		Subject: Test
		-----------------------------------------------------------------------------
		test
		-----------------------------------------------------------------------------
	 *
	 */

	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, int messageNumber, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var line = MModule.repeat(MModule.single("-"), 78, MModule.empty());

		// FIX: ReadMail does not work on multiple messages.

		var actualMail = await parser.Mediator.Send(new GetMailQuery(executor.AsPlayer, messageNumber, "INBOX"));

		if (actualMail is null)
		{
			await parser.NotifyService.Notify(executor, $"You do not have a mail with number: {messageNumber}");
		}

		var messageBuilder = new List<MString>
		{
			line,
			MModule.single($"From: {actualMail!.From.Value.Object()!.Name}"),
			MModule.single($"Date: {actualMail.DateSent:F,25} Folder: {actualMail.Folder,40} Message: {messageNumber,5}"),
			MModule.single($"Status: {(actualMail.Read ? "Read" : "Unread")}"),
			MModule.concat(MModule.single("Subject: "), actualMail.Subject),
			line,
			actualMail.Content,
			line
		};

		var output = MModule.multipleWithDelimiter(MModule.single("\n"), messageBuilder);
		await parser.NotifyService.Notify(executor, output);

		return output;
	}
}