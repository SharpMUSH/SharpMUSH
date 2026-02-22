using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

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
		var line = MModule.repeat(MModule.single("-"), 78, MModule.empty());
		var folder = await MessageListHelper.CurrentMailFolder(parser, objectDataService, executor);

		var actualMail = await mediator.Send(new GetMailQuery(executor.AsPlayer, messageNumber, folder));

		if (actualMail is null)
		{
			await notifyService.Notify(executor, $"MAIL: You do not have a mail with number: {messageNumber + 1}");
			return MModule.single("#-1 NO SUCH MAIL");
		}

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
			MModule.single($"Date: {dateline,-20} Folder: {actualMail.Folder,-20} Message: {messageNumber + 1,5}"),
			MModule.single($"Status: {(actualMail.Read ? "Read" : "Unread")}"),
			MModule.concat(MModule.single("Subject: "), actualMail.Subject),
			line,
			actualMail.Content,
			line
		};

		var output = MModule.multipleWithDelimiter(MModule.single("\n"), messageBuilder);
		await notifyService.Notify(executor, output);

		await mediator.Send(new UpdateMailCommand(actualMail, MailUpdate.ReadEdit(true)));

		return output;
	}
}