using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class RetractMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, IExpandedObjectDataService objectDataService, ILocateService locateService, IMediator mediator, INotifyService notifyService, string target, string msgList)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator!);
		var maybeLocate = await locateService!.LocateAndNotifyIfInvalid(parser, 
			executor, executor, target,
			LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference);

		if (!maybeLocate.IsValid())
		{
			return MModule.single("#-1 NO SUCH PLAYER");
		}

		var sentMails = await MessageListHelper.Handle(parser, objectDataService, mediator, notifyService, MModule.single(msgList), maybeLocate.AsPlayer);
		
		if (sentMails.IsError)
		{
			return MModule.single(sentMails.AsError);
		}
		
		var foundMailList = sentMails.AsMailList;
		
		foreach (var mail in foundMailList)
		{
			if (!mail.Fresh)
			{
				await notifyService!.Notify(executor, "MAIL: Mail already read.");
				continue;
			}
			
			await mediator!.Send(new DeleteMailCommand(mail));
		}

		return MModule.single(foundMailList.Length.ToString());
	}
}