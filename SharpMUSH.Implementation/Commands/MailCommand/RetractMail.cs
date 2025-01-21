using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class RetractMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, string target, string msgList)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var maybeLocate = await parser.LocateService.LocateAndNotifyIfInvalid(parser, 
			executor, executor, target,
			LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference);

		if (!maybeLocate.IsValid())
		{
			return MModule.single("#-1 NO SUCH PLAYER");
		}

		var sentMails = await MessageListHelper.Handle(parser, MModule.single(msgList), maybeLocate.AsPlayer);
		
		if (!sentMails.IsError)
		{
			return MModule.single(sentMails.AsError);
		}
		
		var foundMailList = sentMails.AsMailList;
		
		foreach (var mail in foundMailList)
		{
			if (!mail.Fresh)
			{
				await parser.NotifyService.Notify(executor, "MAIL: Mail already read.");
				continue;
			}
			
			await parser.Mediator.Send(new DeleteMailCommand(mail));
		}

		return MModule.single(foundMailList.Length.ToString());
	}
}