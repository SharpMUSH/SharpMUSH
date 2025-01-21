using System.Collections.Immutable;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class AdminMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, MString? arg0, MString? arg1, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();

		if (!(executor.IsGod() || executor.IsWizard()))
		{
			await parser.NotifyService.Notify(executor, Errors.ErrorPerm);
			return MModule.single(Errors.ErrorPerm);
		}
		
		switch (switches)
		{
			case [.., "DEBUG"]:
				// At this time, this serves no purpose in SharpMUSH.
				await parser.NotifyService.Notify(executor, "MAIL: NOTHING TO DEBUG");
				return MModule.empty();
			case [.., "NUKE"] when executor.IsGod():
				// TODO: This deletes one's own mail, not all mail on the server.
				// A new command is needed.
				var mailList = (await parser.Mediator.Send(new GetAllMailListQuery(executor.AsPlayer))).ToImmutableArray();
				foreach (var mail in mailList)
				{
					await parser.Mediator.Send(new DeleteMailCommand(mail));
				}
				await parser.NotifyService.Notify(executor, "MAIL: Mail deleted.");
				return MModule.single(mailList.Length.ToString());
			default: 
				await parser.NotifyService.Notify(executor, "Invalid arguments for @mail admin command.");
				return MModule.single("#-1 Invalid arguments for @mail admin command.");
		}
	}
}