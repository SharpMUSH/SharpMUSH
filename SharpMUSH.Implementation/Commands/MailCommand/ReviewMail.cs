using Mediator;
using MarkupString;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class ReviewMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, ILocateService locateService, IExpandedObjectDataService objectDataService, IMediator mediator, INotifyService notifyService, MString? arg0, MString? msgListArg, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var line = MarkupText.Plain("-").Repeat(78);
		var name = arg0?.ToPlainText() ?? "all";

		if (executor is not SharpPlayer target)
		{
			throw new InvalidOperationException("@mail/review is run by a player.");
		}

		if (!string.IsNullOrWhiteSpace(arg0?.ToPlainText()))
		{
			// extmail.c's lookup_player resolves "#1" as readily as a name, so a dbref must match too.
			var actualPlayer = await locateService.LocateAndNotifyIfInvalid(parser,
				executor, executor, name,
				LocateFlags.PlayersPreference |
				LocateFlags.MatchWildCardForPlayerName |
				LocateFlags.MatchOptionalWildCardForPlayerName |
				LocateFlags.OnlyMatchTypePreference |
				LocateFlags.AbsoluteMatch);

			if (actualPlayer is not (AnySharpObject and SharpPlayer located))
			{
				await notifyService.Notify(executor, $"MAIL: {name} not found.", executor);
				return MarkupText.Plain(ErrorMessages.Returns.NoSuchPlayer);
			}

			target = located;
		}

		return await MessageListHelper.Handle(parser, objectDataService, mediator, notifyService, msgListArg, target) switch
		{
			IAsyncEnumerable<SharpMail> mailList => await ReviewAsync(notifyService, executor, line, mailList),
			Error<string> error => MarkupText.Plain(error.Value)
		};
	}

	private static async ValueTask<MString> ReviewAsync(INotifyService notifyService, AnySharpObject executor,
		MString line, IAsyncEnumerable<SharpMail> mailList)
	{
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