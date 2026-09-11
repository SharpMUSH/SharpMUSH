using Mediator;
using MarkupString;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class ReadMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser,
		IExpandedObjectDataService objectDataService,
		IMediator mediator,
		INotifyService notifyService,
		int messageNumber, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var line = MarkupText.Plain("-").Repeat(78);
		if (executor is not SharpPlayer player)
		{
			throw new InvalidOperationException("@mail reads a player's own mail, and its dispatcher routes only players here.");
		}

		var folder = await MessageListHelper.CurrentMailFolder(parser, objectDataService, executor);

		var actualMail = await mediator.Send(new GetMailQuery(player, messageNumber, folder));

		if (actualMail is null)
		{
			await notifyService.Notify(executor, $"MAIL: You do not have a mail with number: {messageNumber + 1}", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchMail);
		}

		var dateline = MarkupText.Plain(actualMail.DateSent.ToString("ddd MMM dd HH:mm yyyy")).Pad(MarkupText.Space, 25, PadType.Right, TruncationType.Truncate);

		var mailFrom = await actualMail.From.WithCancellation(CancellationToken.None);
		var messageBuilder = new List<MString>
		{
			line,
			MarkupText.Plain($"From: {mailFrom.Object()!.Name}"),
			MarkupText.Plain($"Date: {dateline,-20} Folder: {actualMail.Folder,-20} Message: {messageNumber + 1,5}"),
			MarkupText.Plain($"Status: {(actualMail.Read ? "Read" : "Unread")}"),
			MarkupText.Concat(MarkupText.Plain("Subject: "), actualMail.Subject),
			line,
			actualMail.Content,
			line
		};

		var output = MarkupText.Join(MarkupText.NewLine, messageBuilder);
		await notifyService.Notify(executor, output, executor);

		await mediator.Send(new UpdateMailCommand(actualMail, MailUpdate.ReadEdit(true)));

		return output;
	}
}