using System.Collections.Immutable;
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class AdminMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, IMediator? mediator, INotifyService? notifyService, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator!);

		if (!(executor.IsGod() || await executor.IsWizard()))
		{
			await notifyService!.Notify(executor, Errors.ErrorPerm);
			return MModule.single(Errors.ErrorPerm);
		}
		
		switch (switches)
		{
			case [.., "DEBUG"]:
				// At this time, this serves no purpose in SharpMUSH.
				await notifyService!.Notify(executor, "MAIL: NOTHING TO DEBUG");
				return MModule.single("MAIL: NOTHING TO DEBUG");
			case [.., "NUKE"] when executor.IsGod():
				// TODO: This deletes one's own mail, not all mail on the server.
				// A new command is needed.
				var mailList = mediator!.CreateStream(new GetAllMailListQuery(executor.AsPlayer));
				var length = 0;
				await foreach (var mail in mailList)
				{
					await mediator.Send(new DeleteMailCommand(mail));
					length++;
				}
				await notifyService!.Notify(executor, "MAIL: Mail deleted.");
				return MModule.single(length.ToString());
			default: 
				await notifyService!.Notify(executor, "Invalid arguments for @mail admin command.");
				return MModule.single("#-1 Invalid arguments for @mail admin command.");
		}
	}
}