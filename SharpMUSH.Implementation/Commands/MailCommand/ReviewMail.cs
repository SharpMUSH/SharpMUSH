﻿using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class ReviewMail
{
	public static async ValueTask<MString>  Handle(IMUSHCodeParser parser, MString? arg0, MString? msgListArg, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var line = MModule.repeat(MModule.single("-"), 78, MModule.empty());
		var name = arg0?.ToPlainText() ?? "all";
		
		var target = executor.AsPlayer;

		if (string.IsNullOrWhiteSpace(arg0?.ToPlainText()))
		{
			var actualPlayer = await parser.LocateService.LocateAndNotifyIfInvalid(parser, 
				executor, executor, name,
				LocateFlags.PlayersPreference |
				LocateFlags.MatchWildCardForPlayerName |
				LocateFlags.MatchOptionalWildCardForPlayerName |
				LocateFlags.OnlyMatchTypePreference);

			if (!actualPlayer.IsPlayer)
			{
				await parser.NotifyService.Notify(executor, $"MAIL: {name} not found.");
				return MModule.single("#-1 NO SUCH PLAYER");
			}
		
			target = actualPlayer.AsPlayer;
		}
		
		var maybeMailList = await MessageListHelper.Handle(parser, msgListArg, target);
		
		if (!maybeMailList.IsError)
		{
			return MModule.single(maybeMailList.AsError);
		}
		
		var mailList = maybeMailList.AsMailList;
		var i = 0;
		
		foreach (var actualMail in mailList)
		{
			i++;
			var dateline = MModule.pad(
				MModule.single(actualMail.DateSent.ToString("ddd MMM dd HH:mm yyyy")),
				MModule.single(" "),
				25,
				MModule.PadType.Right,
				MModule.TruncationType.Truncate);

			var mailFrom = await actualMail.From.WithCancellation(CancellationToken.None);
			var messageBuilder = new List<MString>
			{
				line,
				MModule.single($"From: {mailFrom.Object()!.Name}"),
				MModule.single($"Date: {dateline,-20} Folder: {actualMail.Folder,-20} Message: {i,5}"),
				MModule.single($"Status: {(actualMail.Read ? "Read" : "Unread")}"),
				MModule.concat(MModule.single("Subject: "), actualMail.Subject),
				line,
				actualMail.Content,
				line
			};

			var output = MModule.multipleWithDelimiter(MModule.single("\n"), messageBuilder);
			await parser.NotifyService.Notify(executor, output);
		}
		
		return MModule.empty();
	}
}