using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class StatsMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser,
		IExpandedObjectDataService objectDataService,
		ILocateService locateService,
		IMediator mediator,
		INotifyService notifyService,
		MString? arg0, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		if (executor is not SharpPlayer target)
		{
			throw new InvalidOperationException("@mail reads a player's own mail, and its dispatcher routes only players here.");
		}

		if (!string.IsNullOrEmpty(arg0?.ToPlainText()))
		{
			if (!await executor.IsWizard())
			{
				var errorResult = await notifyService.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.PermissionDenied,
					shouldNotify: true);
				return errorResult.Message!;
			}

			switch (await locateService.LocateAndNotifyIfInvalid(
				parser, executor, executor, arg0.ToPlainText(),
				LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference))
			{
				case AnySharpObject and SharpPlayer located:
					target = located;
					break;
				case AnySharpObject:
					throw new InvalidOperationException("A lookup restricted to players found something that is not a player.");
				case None:
					var noTargetError = await notifyService.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.NoSuchObject,
						notifyMessage: ErrorMessages.Notifications.CantSeeThat,
						shouldNotify: true);
					return noTargetError.Message!;
				case Error<string> error:
					return MarkupText.Plain(error.Value);
			}
		}

		switch (switches)
		{
			case ["CSTATS"]:
				return await CStats(parser, objectDataService, mediator, notifyService, executor, target);
		}

		var allSentMail = mediator.CreateStream(new GetAllSentMailListQuery(target.Object));
		var allReceivedMail = mediator.CreateStream(new GetAllMailListQuery(target));
		var targetName = target.Object.Name;

		return switches switch
		{
			["STATS"] => await Stats(notifyService, executor, targetName, allSentMail, allReceivedMail),
			["DSTATS"] => await DStats(notifyService, executor, targetName, allSentMail, allReceivedMail),
			["FSTATS"] => await FStats(notifyService, executor, targetName, allSentMail, allReceivedMail),
			_ => MarkupText.Empty
		};
	}

	private static async Task<MString> CStats(IMUSHCodeParser parser,
		IExpandedObjectDataService objectDataService,
		IMediator mediator,
		INotifyService notifyService, AnySharpObject executor, SharpPlayer target)
	{
		var currentFolder = await MessageListHelper.CurrentMailFolder(parser, objectDataService, executor);
		var stats = await mediator.CreateStream(new GetMailListQuery(target, currentFolder)).ToArrayAsync();
		var unread = stats.Count(x => !x.Read);
		var cleared = stats.Count(x => x.Cleared);

		await notifyService.Notify(executor,
			$"MAIL: {stats.Length} messages in folder [{currentFolder}] ({unread} unread, {cleared} cleared).", executor);

		return MarkupText.Empty;
	}

	private static async Task<MString> FStats(
		INotifyService notifyService, AnySharpObject executor, string targetName,
		IAsyncEnumerable<SharpMail> allSentMailIe, IAsyncEnumerable<SharpMail> allReceivedMailIe)
	{
		await notifyService.Notify(executor, $"Mail statistics for {targetName}:", executor);

		var allSentMail = await allSentMailIe.ToArrayAsync();
		var sentSize = allSentMail.Sum(x => x.Content.Length);
		var sentUnread = allSentMail.Count(x => !x.Read);
		var sentCleared = allSentMail.Count(x => x.Cleared);

		await notifyService.Notify(executor,
			$"{allSentMail.Length} messages sent, {sentUnread} unread, {sentCleared} cleared, totalling {sentSize} characters.", executor);

		var allReceivedMail = await allReceivedMailIe.ToArrayAsync();
		var receivedSize = allReceivedMail.Sum(x => x.Content.Length);
		var receivedUnread = allReceivedMail.Count(x => !x.Read);
		var receivedCleared = allReceivedMail.Count(x => x.Cleared);

		await notifyService.Notify(executor,
			$"{allReceivedMail.Length} messages received, {receivedUnread} unread, {receivedCleared} cleared, totalling {receivedSize} characters.", executor);
		if (allReceivedMail.Length > 0)
			await notifyService.Notify(executor, $"Last is dated {allReceivedMail.Max(x => x.DateSent)}", executor);

		return MarkupText.Empty;
	}

	private static async Task<MString> DStats(
		INotifyService notifyService, AnySharpObject executor, string targetName,
		IAsyncEnumerable<SharpMail> allSentMailIe, IAsyncEnumerable<SharpMail> allReceivedMailIe)
	{
		var allSentMail = await allSentMailIe.ToArrayAsync();
		var sentUnread = allSentMail.Count(x => !x.Read);
		var sentCleared = allSentMail.Count(x => x.Cleared);
		await notifyService.Notify(executor, $"Mail statistics for {targetName}:", executor);
		await notifyService.Notify(executor,
			$"{allSentMail.Length} messages sent, {sentUnread} unread, {sentCleared} cleared.", executor);

		var allReceivedMail = await allReceivedMailIe.ToArrayAsync();
		var receivedUnread = allReceivedMail.Count(x => !x.Read);
		var receivedCleared = allReceivedMail.Count(x => x.Cleared);

		await notifyService.Notify(executor,
			$"{allReceivedMail.Length} messages received, {receivedUnread} unread, {receivedCleared} cleared.", executor);
		if (allReceivedMail.Length > 0)
			await notifyService.Notify(executor, $"Last is dated {allReceivedMail.Max(x => x.DateSent)}", executor);

		return MarkupText.Empty;
	}

	private static async Task<MString> Stats(
		INotifyService notifyService, AnySharpObject executor, string targetName,
		IAsyncEnumerable<SharpMail> allSentMailIe, IAsyncEnumerable<SharpMail> allReceivedMailIe)
	{
		await notifyService.Notify(executor, $"{targetName} sent {await allSentMailIe.CountAsync()} messages.", executor);
		await notifyService.Notify(executor, $"{targetName} received {await allReceivedMailIe.CountAsync()} messages.", executor);

		return MarkupText.Empty;
	}
}