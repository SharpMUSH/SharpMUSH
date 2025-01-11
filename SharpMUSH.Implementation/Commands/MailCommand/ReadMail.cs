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
	
	public static async ValueTask<MString>  Handle(IMUSHCodeParser parser, MString arg0, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var msgListString = arg0.ToPlainText();
		var msgList = msgListString.Split(' ');
		var line = MModule.repeat(MModule.single("-"), 78, MModule.empty());
		
		/*  A <msg-list> is one of the following:
					A single msg # (ex: 3)
	        A message range (ex: 2-5, -7, 3-)
	        A folder number and message number/range (ex: 0:3, 1:2-5, 2:-7)
	        A sender (ex: *paul)
	        An age of mail in days (ex: ~3 (exactly 3), <2, >1)
	           "days" here means 24-hour periods from the current time.
	        One of the following: "read", "unread", "cleared", "tagged", "urgent", "folder" 
						(all messages in the current folder), "all" (all messages in all folders).
		 */

		var successfulReads = new List<string>();
		foreach (var msg in msgList)
		{
			if (!int.TryParse(msg, out var messageNumber))
			{
				await parser.NotifyService.Notify(executor, $"Invalid message number: {msg}");
			}
			
			var actualMail = await parser.Mediator.Send(new GetMailQuery(executor.AsPlayer, messageNumber, "INBOX"));

			if (actualMail is null)
			{
				await parser.NotifyService.Notify(executor, $"You do not have a mail with number: {messageNumber}");
			}

			// TODO: Line up Date, Folder, Message 
			var messageBuilder = new List<MString>
			{
				line,
				MModule.single($"From: {actualMail!.From.Value.Object()!.Name}"),
				MModule.single($"Date: {actualMail.DateSent:F} Folder: {actualMail.Folder} Message: {messageNumber}"),
				MModule.single($"Status: {(actualMail.Read ? "Read" : "Unread")}"),
				MModule.concat(MModule.single("Subject: "),actualMail.Subject),
				line,
				actualMail.Content,
				line
			};
			
			successfulReads.Add(msg);
			var output = MModule.multipleWithDelimiter(MModule.single("\n"), messageBuilder);
			await parser.NotifyService.Notify(executor, output);
		}
		
		return MModule.single(string.Join(' ', successfulReads));
	}
}