using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class ForwardMail
{
	public static async ValueTask<MString>  Handle(IMUSHCodeParser parser, int mailNumber, string target)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var maybeLocate = await parser.LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, target,
			LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference);
		var currentFolder = await MessageListHelper.CurrentMailFolder(parser, executor);
		
		if (!maybeLocate.IsValid())
		{
			return MModule.single("#-1 NO SUCH PLAYER");
		}
		
		var targetPlayer = maybeLocate.AsPlayer;
		var mail = await parser.Mediator.Send(new GetMailQuery(executor.AsPlayer, mailNumber, currentFolder));

		if (mail is null)
		{
			return MModule.single("#-1 MAIL NOT FOUND");
		}

		mail.Forwarded = true;
		mail.Subject = MModule.concat(MModule.single("Fwd: "), mail.Subject);
		mail.DateSent = DateTimeOffset.UtcNow;
		
		await parser.Mediator.Send(new SendMailCommand(executor.Object(), targetPlayer, mail));
		
		return MModule.single(targetPlayer.Object.DBRef.ToString());
	}
}