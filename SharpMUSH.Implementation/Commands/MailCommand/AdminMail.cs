using SharpMUSH.Library.Authorization;
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class AdminMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, IMediator? mediator, INotifyService? notifyService,
		ILocateService locateService, MString? arg0, MString? arg1, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator!);

		if (!await executor.Can(PortalPermission.ChatAdmin))
		{
			var errorResult = await notifyService!.PermissionDeniedNeeds(executor, PortalPermission.ChatAdmin);
			return errorResult.Message;
		}

		switch (switches)
		{
			case [.., "DEBUG"]:
				return await DebugAsync(parser, mediator!, notifyService!, locateService, executor,
					arg0?.ToPlainText().Trim() ?? string.Empty, arg1?.ToPlainText().Trim() ?? string.Empty);
			case [.., "NUKE"] when executor.IsGod():
				var allMailList = mediator!.CreateStream(new GetAllSystemMailQuery());
				var totalCount = 0;
				await foreach (var mail in allMailList)
				{
					await mediator.Send(new DeleteMailCommand(mail));
					totalCount++;

					if (totalCount % 100 == 0)
					{
						await notifyService!.Notify(executor, $"MAIL: Deleted {totalCount} messages so far...");
					}
				}
				await notifyService!.Notify(executor, $"MAIL: All mail deleted from system. Total: {totalCount}");
				return MarkupText.Plain(totalCount.ToString());
			default:
				await notifyService!.Notify(executor, "Invalid arguments for @mail admin command.");
				return MarkupText.Plain(ErrorMessages.Returns.InvalidMailArguments);
		}
	}

	/// <summary>PennMUSH <c>do_mail_debug</c> (<c>extmail.c:1743</c>).</summary>
	private static async ValueTask<MString> DebugAsync(IMUSHCodeParser parser, IMediator mediator, INotifyService notifyService,
		ILocateService locateService, AnySharpObject executor, string action, string victim)
	{
		switch (action.ToUpperInvariant())
		{
			case "CLEAR":
				return await locateService.LocatePlayer(parser, executor, executor, victim) switch
				{
					AnySharpObject and SharpPlayer player => await ClearAsync(mediator, notifyService, executor, player),
					_ => await NotifyAsync(notifyService, executor, $"{victim}: No such player.")
				};
			case "SANITY":
				await foreach (var mail in mediator.CreateStream(new GetAllMailRecipientsQuery()))
				{
					if (await mediator.Send(new GetObjectNodeQuery(new DBRef(mail.Recipient))) is not AnySharpObject recipient)
					{
						await notifyService.Notify(executor, $"Bad object #{mail.Recipient} has mail.");
					}
					else if (!recipient.IsPlayer)
					{
						await notifyService.Notify(executor, $"{recipient.Object().Name}(#{mail.Recipient}) has mail but is not a player.");
					}
				}

				return await NotifyAsync(notifyService, executor, "Mail sanity check completed.");
			case "FIX":
				await foreach (var mail in mediator.CreateStream(new GetAllMailRecipientsQuery()))
				{
					if (await mediator.Send(new GetObjectNodeQuery(new DBRef(mail.Recipient))) is AnySharpObject { IsPlayer: true })
					{
						continue;
					}

					await notifyService.Notify(executor, $"Fixing mail for #{mail.Recipient}.");
					await mediator.Send(new DeleteMailByIdCommand(mail.Id));
				}

				return await NotifyAsync(notifyService, executor, "Mail sanity fix completed.");
			default:
				return await NotifyAsync(notifyService, executor, "That is not a debugging option.");
		}
	}

	/// <summary><c>do_mail_clear(target, "ALL")</c> then <c>do_mail_purge(target)</c>: every message in every folder.</summary>
	private static async ValueTask<MString> ClearAsync(IMediator mediator, INotifyService notifyService, AnySharpObject executor,
		SharpPlayer player)
	{
		await foreach (var mail in mediator.CreateStream(new GetAllMailListQuery(player)))
		{
			await mediator.Send(new DeleteMailCommand(mail));
		}

		return await NotifyAsync(notifyService, executor, $"Mail cleared for {player.Object.Name}(#{player.Object.DBRef.Number}).");
	}

	private static async ValueTask<MString> NotifyAsync(INotifyService notifyService, AnySharpObject executor, string message)
	{
		await notifyService.Notify(executor, message);
		return MarkupText.Plain(message);
	}
}