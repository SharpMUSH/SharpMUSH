﻿using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using Errors = SharpMUSH.Library.Definitions.Errors;

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
		var executor = await parser.CurrentState.KnownExecutorObject(mediator!);
		var target = executor;

		if (!string.IsNullOrEmpty(arg0?.ToPlainText()))
		{
			if (!(executor.IsGod() || await executor.IsWizard()))
			{
				await notifyService!.Notify(executor, Errors.ErrorPerm);
				return MModule.single(Errors.ErrorPerm);
			}

			var maybeTarget = await locateService!.LocateAndNotifyIfInvalid(
				parser, executor, executor, arg0.ToPlainText(),
				LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference);

			if (maybeTarget.IsError)
			{
				return MModule.single(maybeTarget.AsError.Value);
			}

			if (maybeTarget.IsNone)
			{
				return MModule.single(Errors.ErrorCantSeeThat);
			}

			target = maybeTarget.AsPlayer;
		}

		switch (switches)
		{
			case ["CSTATS"]:
				return await CStats(parser, objectDataService, mediator, notifyService, executor, target);
		}

		var allSentMail = await mediator!.Send(new GetAllSentMailListQuery(target.Object()));
		var allReceivedMail = await mediator!.Send(new GetAllMailListQuery(target.AsPlayer));
		var targetName = target.Object().Name;

		return switches switch
		{
			["STATS"] => await Stats(notifyService, executor, targetName, allSentMail, allReceivedMail),
			["DSTATS"] => await DStats(notifyService, executor, targetName, allSentMail, allReceivedMail),
			["FSTATS"] => await FStats(notifyService, executor, targetName, allSentMail, allReceivedMail),
			_ => MModule.empty()
		};
	}

	private static async Task<MString> CStats(IMUSHCodeParser parser,
		IExpandedObjectDataService objectDataService,
		IMediator mediator,
		INotifyService notifyService, AnySharpObject executor, AnySharpObject target)
	{
		var currentFolder = await MessageListHelper.CurrentMailFolder(parser, objectDataService, executor);
		var stats = (await mediator!.Send(new GetMailListQuery(target.AsPlayer, currentFolder))).ToArray();
		var unread = stats.Sum(x => x.Read ? 0 : 1);
		var cleared = stats.Sum(x => x.Cleared ? 1 : 0);

		await notifyService!.Notify(executor,
			$"MAIL: {stats.Length} messages in folder [{currentFolder}] ({unread} unread, {cleared} cleared).");

		return MModule.empty();
	}

	private static async Task<MString> FStats(
		INotifyService notifyService, AnySharpObject executor, string targetName,
		IEnumerable<SharpMail> allSentMailIe, IEnumerable<SharpMail> allReceivedMailIe)
	{
		await notifyService!.Notify(executor, $"Mail statistics for {targetName}:");

		var allSentMail = allSentMailIe.ToArray();
		var sentSize = allSentMail.Sum(x => x.Content.Length);
		var sentUnread = allSentMail.Sum(x => x.Read ? 0 : 1);
		var sentCleared = allSentMail.Sum(x => x.Cleared ? 1 : 0);

		await notifyService!.Notify(executor,
			$"{allSentMail.Length} messages sent, {sentUnread} unread, {sentCleared} cleared, totalling {sentSize} characters.");

		var allReceivedMail = allReceivedMailIe.ToArray();
		var receivedSize = allReceivedMail.Sum(x => x.Content.Length);
		var receivedUnread = allReceivedMail.Sum(x => x.Read ? 0 : 1);
		var receivedCleared = allReceivedMail.Sum(x => x.Cleared ? 1 : 0);
		var lastDate = allReceivedMail.Max(x => x.DateSent);

		await notifyService!.Notify(executor,
			$"{allReceivedMail.Length} messages received, {receivedUnread} unread, {receivedCleared} cleared, totalling {receivedSize} characters.");
		await notifyService!.Notify(executor, $"Last is dated {lastDate}");

		return MModule.empty();
	}

	private static async Task<MString> DStats(
		INotifyService notifyService, AnySharpObject executor, string targetName,
		IEnumerable<SharpMail> allSentMailIe, IEnumerable<SharpMail> allReceivedMailIe)
	{
		var allSentMail = allSentMailIe.ToArray();
		var sentUnread = allSentMail.Sum(x => x.Read ? 0 : 1);
		var sentCleared = allSentMail.Sum(x => x.Cleared ? 1 : 0);
		await notifyService!.Notify(executor, $"Mail statistics for {targetName}:");
		await notifyService!.Notify(executor,
			$"{allSentMail.Length} messages sent, {sentUnread} unread, {sentCleared} cleared.");

		var allReceivedMail = allReceivedMailIe.ToArray();
		var receivedUnread = allReceivedMail.Sum(x => x.Read ? 0 : 1);
		var receivedCleared = allReceivedMail.Sum(x => x.Cleared ? 1 : 0);
		var lastDate = allReceivedMail.Max(x => x.DateSent);

		await notifyService!.Notify(executor,
			$"{allReceivedMail.Length} messages received, {receivedUnread} unread, {receivedCleared} cleared.");
		await notifyService!.Notify(executor, $"Last is dated {lastDate}");

		return MModule.empty();
	}

	private static async Task<MString> Stats(
		INotifyService notifyService, AnySharpObject executor, string targetName,
		IEnumerable<SharpMail> allSentMailIe, IEnumerable<SharpMail> allReceivedMailIe)
	{
		await notifyService!.Notify(executor, $"{targetName} sent {allSentMailIe.Count()} messages.");
		await notifyService!.Notify(executor, $"{targetName} received {allReceivedMailIe.Count()} messages.");

		return MModule.empty();
	}
}