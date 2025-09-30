using System.Globalization;
using DotNext;
using Humanizer;
using Mediator;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class ListMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, IExpandedObjectDataService objectDataService, IMediator? mediator, INotifyService? notifyService, MString? arg0, MString? arg1, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator!);
		var line = MModule.repeat(MModule.single("-"), 78, MModule.empty());

		var filteredList = await MessageListHelper.Handle(parser, objectDataService, mediator, notifyService, arg0, executor);

		if (filteredList.IsError)
		{
			await notifyService!.Notify(executor, filteredList.AsT0.Value);
			return MModule.single(filteredList.AsT0.Value);
		}

		var list = filteredList.AsT1;

		if (list.IsNullOrEmpty())
		{
			await notifyService!.Notify(executor, "MAIL: You have no matching mail in that mail folder.");
			return MModule.single("MAIL: You have no matching mail in that mail folder.");
		}

		foreach (var folder in list.GroupBy(x => x.Folder))
		{
			var center = MModule.pad(
				markupStr: MModule.single($"  MAIL (folder {folder.Key})  "),
				padStr: MModule.single("-"),
				width: 78,
				MModule.PadType.Center,
				MModule.TruncationType.Truncate);

			var folderTasks = await folder.ToAsyncEnumerable().Select((x,y,_) => DisplayMailLine(x,y)).ToArrayAsync();
			
			MString[] builder =
			[
				center,
				.. folderTasks,
				line
			];
			await notifyService!.Notify(executor, MModule.multipleWithDelimiter(MModule.single("\n"), builder));
		}

		return MModule.empty();
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
		var subject = mail.Subject.ToString().Truncate(30);

		var result =
			$"[{read}{cleared}{urgent}{forwarded}{tagged}]  {arg2 + 1,-5} {fromName,-15} {subject,-30} {date,-16}";
		return MModule.single(result);
	}
}