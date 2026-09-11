using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class ForwardMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser,
		IExpandedObjectDataService objectDataService,
		ILocateService? locateService,
		IPermissionService? permissionService,
		IMediator? mediator,
		int mailNumber, string target)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator!);
		if (executor is not SharpPlayer executorPlayer)
		{
			throw new InvalidOperationException("@mail reads a player's own mail, and its dispatcher routes only players here.");
		}

		var maybeLocate = await locateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, target,
			LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference);
		var currentFolder = await MessageListHelper.CurrentMailFolder(parser, objectDataService, executor);

		return maybeLocate switch
		{
			AnySharpObject and SharpPlayer targetPlayer => await ForwardAsync(permissionService!, mediator!, executorPlayer,
				targetPlayer, mailNumber, currentFolder),
			AnySharpObject => MarkupText.Plain("MAIL: Cannot forward to non-player."),
			Error<CallState> error => error.Value.Message!
		};
	}

	private static async ValueTask<MString> ForwardAsync(IPermissionService permissionService, IMediator mediator,
		SharpPlayer executor, SharpPlayer targetPlayer, int mailNumber, string currentFolder)
	{
		if (!await permissionService.PassesLock(executor, targetPlayer, LockType.Mail))
		{
			return MarkupText.Plain($"MAIL: {targetPlayer.Object.Name} does not wish to receive mail from you.");
		}

		var mail = await mediator.Send(new GetMailQuery(executor, mailNumber, currentFolder));

		if (mail is null)
		{
			return MarkupText.Plain(ErrorMessages.Returns.MailNotFound);
		}

		mail.Forwarded = true;
		mail.Subject = MarkupText.Concat(MarkupText.Plain("Fwd: "), mail.Subject);
		mail.DateSent = DateTimeOffset.UtcNow;

		await mediator.Send(new SendMailCommand(executor.Object, targetPlayer, mail));

		return MarkupText.Plain(targetPlayer.Object.DBRef.ToString());
	}
}