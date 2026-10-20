using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class PurgeMail
{
	/// <summary>
	/// <c>do_mail_purge</c> (<c>extmail.c</c>): deletes every message in every folder of the executor's
	/// mailbox that <c>@mail/clear</c> marked as cleared. Returns how many were deleted.
	/// </summary>
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, IMediator mediator, INotifyService notifyService)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var purged = 0;
		if (executor is SharpPlayer player)
		{
			var cleared = await mediator.CreateStream(new GetAllMailListQuery(player)).Where(mail => mail.Cleared).ToListAsync();
			foreach (var mail in cleared)
			{
				await mediator.Send(new DeleteMailCommand(mail));
				purged++;
			}
		}

		await notifyService.Notify(executor, "MAIL: Mailbox purged.", executor);
		return MarkupText.Plain(purged.ToString());
	}
}
