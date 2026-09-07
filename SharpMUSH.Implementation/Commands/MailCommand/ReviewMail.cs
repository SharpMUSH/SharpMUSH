using Mediator;
using MarkupString;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class ReviewMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, ILocateService locateService, IExpandedObjectDataService objectDataService, IMediator mediator, INotifyService notifyService, MString? arg0, MString? msgListArg, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var line = MarkupText.Plain("-").Repeat(78);
		var name = arg0?.ToPlainText() ?? "all";

		var target = executor.AsPlayer;

		if (string.IsNullOrWhiteSpace(arg0?.ToPlainText()))
		{
			var actualPlayer = await locateService.LocateAndNotifyIfInvalid(parser,
				executor, executor, name,
				LocateFlags.PlayersPreference |
				LocateFlags.MatchWildCardForPlayerName |
				LocateFlags.MatchOptionalWildCardForPlayerName |
				LocateFlags.OnlyMatchTypePreference);

			if (!actualPlayer.IsPlayer)
			{
				await notifyService.Notify(executor, $"MAIL: {name} not found.", executor);
				return MarkupText.Plain(ErrorMessages.Returns.NoSuchPlayer);
			}

			target = actualPlayer.AsPlayer;
		}

		var maybeMailList = await MessageListHelper.Handle(parser, objectDataService, mediator, notifyService, msgListArg, target);

		if (!maybeMailList.IsError)
		{
			return MarkupText.Plain(maybeMailList.AsError);
		}

		var mailList = maybeMailList.AsMailList;
		var i = 0;

		await foreach (var actualMail in mailList)
		{
			i++;
			var dateline = MarkupText.Plain(actualMail.DateSent.ToString("ddd MMM dd HH:mm yyyy")).Pad(MarkupText.Space, 25, PadType.Right, TruncationType.Truncate);

			var mailFrom = await actualMail.From.WithCancellation(CancellationToken.None);
			var messageBuilder = new List<MString>
			{
				line,
				MarkupText.Plain($"From: {mailFrom.Object()!.Name}"),
				MarkupText.Plain($"Date: {dateline,-20} Folder: {actualMail.Folder,-20} Message: {i,5}"),
				MarkupText.Plain($"Status: {(actualMail.Read ? "Read" : "Unread")}"),
				MarkupText.Concat(MarkupText.Plain("Subject: "), actualMail.Subject),
				line,
				actualMail.Content,
				line
			};

			var output = MarkupText.Join(MarkupText.NewLine, messageBuilder);
			await notifyService.Notify(executor, output, executor);
		}

		return MarkupText.Empty;
	}
}