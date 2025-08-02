using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class ForwardMail
{
	public static async ValueTask<MString>  Handle(IMUSHCodeParser parser, int mailNumber, string target)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var maybeLocate = await parser.LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, target,
			LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference);
		var currentFolder = await MessageListHelper.CurrentMailFolder(parser, executor);
		
		if (maybeLocate.IsError)
		{
			return maybeLocate.AsError.Message!;
		}

		if (!maybeLocate.AsSharpObject.IsPlayer)
		{
			return MModule.single("MAIL: Cannot forward to non-player.");
		}
		
		var targetPlayer = maybeLocate.AsSharpObject.AsPlayer;
		
		if (!parser.PermissionService.PassesLock(executor, targetPlayer, LockType.Mail))
		{
			return MModule.single($"MAIL: {targetPlayer.Object.Name} does not wish to receive mail from you.");
		}
		
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