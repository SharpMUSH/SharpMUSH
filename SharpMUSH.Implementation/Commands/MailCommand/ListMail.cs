using Humanizer;
using Mediator;
using MarkupString;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Globalization;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class ListMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, IExpandedObjectDataService objectDataService, IMediator? mediator, INotifyService? notifyService, MString? arg0, MString? arg1, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator!);
		var line = MarkupText.Plain("-").Repeat(78);

		return await MessageListHelper.Handle(parser, objectDataService, mediator, notifyService, arg0, executor) switch
		{
			IAsyncEnumerable<SharpMail> list => await ListAsync(notifyService!, executor, line, list),
			Error<string> error => await MessageListHelper.RefuseAsync(notifyService!, executor, error.Value)
		};
	}

	private static async ValueTask<MString> ListAsync(INotifyService notifyService, AnySharpObject executor,
		MString line, IAsyncEnumerable<SharpMail> list)
	{
		var foundAny = false;
		await foreach (var folder in list.GroupBy(x => x.Folder))
		{
			var center = MarkupText.Plain($"  MAIL (folder {folder.Key})  ").Pad(MarkupText.Plain("-"), 78, PadType.Center, TruncationType.Truncate);

			var folderTasks = await folder.ToAsyncEnumerable().Select((x, y, _) => DisplayMailLine(x, y)).ToArrayAsync();

			MString[] builder =
			[
				center,
				.. folderTasks,
				line
			];
			await notifyService.Notify(executor, MarkupText.Join(MarkupText.NewLine, builder));

			foundAny = true;
		}

		if (foundAny)
		{
			return MarkupText.Empty;
		}

		await notifyService.Notify(executor, "MAIL: You have no matching mail in that mail folder.");
		return MarkupText.Plain("MAIL: You have no matching mail in that mail folder.");
	}

	private static async ValueTask<MString> DisplayMailLine(SharpMail mail, int arg2)
	{
		var read = mail.Read ? "-" : "N";
		var cleared = mail.Cleared ? "C" : "-";
		var urgent = mail.Urgent ? "U" : "-";
		var forwarded = mail.Forwarded ? "F" : "-";
		var tagged = mail.Tagged ? "+" : "-";
		var date = mail.DateSent.ToString("ddd MMM dd HH:mm", CultureInfo.InvariantCulture);
		var fromName = (await mail.From.WithCancellation(CancellationToken.None)).Object()!.Name.Truncate(15);
		var subject = mail.Subject.ToPlainText().Truncate(30);

		var result =
			$"[{read}{cleared}{urgent}{forwarded}{tagged}]  {arg2 + 1,-5} {fromName,-15} {subject,-30} {date,-16}";
		return MarkupText.Plain(result);
	}
}