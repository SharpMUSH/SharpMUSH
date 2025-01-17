using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class StatsMail
{
	public static async ValueTask<MString>  Handle(IMUSHCodeParser parser, MString? arg0, MString? arg1, string[] switches)
	{
		/*
		 * STATS: 
		Liminality sent 59 messages.
		Liminality has 145 messages.

		* DSTATS:
		Mail statistics for Liminality:
		59 messages sent, 12 unread, 0 cleared.
		145 messages received, 102 unread, 0 cleared.
		Last is dated Mon Jul 11 02:47:26 2022

		* FSTATS:
		Mail statistics for Liminality:
		59 messages sent, 12 unread, 0 cleared, totalling 10000 characters.
		145 messages received, 102 unread, 0 cleared, totalling 24638 characters.
		Last is dated Mon Jul 11 02:47:26 2022
		
		* CSTATS:
		MAIL: 145 messages in folder 0 [INBOX] (102 unread, 0 cleared).
		*/
		
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}
}