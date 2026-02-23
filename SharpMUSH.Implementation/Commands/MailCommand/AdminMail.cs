using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
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
			var errorResult = await notifyService!.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.PermissionDenied,
				shouldNotify: true);
			return errorResult.Message!;
		}

		switch (switches)
		{
			case [.., "DEBUG"]:
				// At this time, this serves no purpose in SharpMUSH.
				await notifyService!.Notify(executor, "MAIL: NOTHING TO DEBUG");
				return MModule.single("MAIL: NOTHING TO DEBUG");
			case [.., "NUKE"] when executor.IsGod():
				// Delete ALL mail in the entire system (God-only operation)
				// Note: This operation may take time for large mail systems
				var allMailList = mediator!.CreateStream(new GetAllSystemMailQuery());
				var totalCount = 0;
				await foreach (var mail in allMailList)
				{
					await mediator.Send(new DeleteMailCommand(mail));
					totalCount++;

					// Provide progress feedback for large deletions
					if (totalCount % 100 == 0)
					{
						await notifyService!.Notify(executor, $"MAIL: Deleted {totalCount} messages so far...");
					}
				}
				await notifyService!.Notify(executor, $"MAIL: All mail deleted from system. Total: {totalCount}");
				return MModule.single(totalCount.ToString());
			default:
				await notifyService!.Notify(executor, "Invalid arguments for @mail admin command.");
				return MModule.single("#-1 Invalid arguments for @mail admin command.");
		}
	}
}