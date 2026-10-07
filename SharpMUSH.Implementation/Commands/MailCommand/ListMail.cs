using Mediator;
using MarkupString;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;
using System.Globalization;
using MarkupString.Layout;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class ListMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, IExpandedObjectDataService objectDataService, IMediator? mediator, INotifyService? notifyService, MString? arg0, MString? arg1, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator!);

		var folders = executor is SharpPlayer player
			? await MailFolders.LoadAsync(objectDataService, player)
			: new ExpandedMailData();

		return await MessageListHelper.Handle(parser, objectDataService, mediator, notifyService, arg0, executor) switch
		{
			IAsyncEnumerable<SharpMail> list => await ListAsync(notifyService!, executor, folders, list),
			Error<string> error => await MessageListHelper.RefuseAsync(notifyService!, executor, error.Value)
		};
	}

	/// <summary>The columns of a folder's listing; a narrow client loses the date, then the flags.</summary>
	private static readonly ImmutableArray<TableColumn> Columns =
	[
		new(MarkupText.Plain("#")) { Alignment = Alignment.Right, Wrap = false },
		new(MarkupText.Plain("Flags")) { Wrap = false, Priority = 2 },
		new(MarkupText.Plain("From")) { Min = 8, Max = 20 },
		new(MarkupText.Plain("Subject")) { Min = 10 },
		new(MarkupText.Plain("Sent")) { Wrap = false, Priority = 3 },
	];

	private static async ValueTask<MString> ListAsync(INotifyService notifyService, AnySharpObject executor,
		ExpandedMailData folders, IAsyncEnumerable<SharpMail> list)
	{
		var foundAny = false;
		await foreach (var folder in list.GroupBy(x => x.Folder))
		{
			// do_mail_list (extmail.c:762) heads the list with the folder's number.
			var title = folders.NumberOf(folder.Key) is int number ? $"Mail, folder {number} ({folder.Key})" : $"Mail, folder {folder.Key}";
			var rows = await folder.ToAsyncEnumerable().Select((mail, index, _) => Row(mail, index)).ToArrayAsync();
			var unread = folder.Count(mail => !mail.Read);

			await notifyService.Notify(executor, ServerLayout.Build(ServerLayout.Section(MarkupText.Plain(title),
				ServerLayout.Listing(Columns, rows),
				new Rule(),
				new TextBlock(MarkupText.Plain($"{Counted(rows.Length, "message")}, {unread} unread.\nFlags: N new, U urgent, F forwarded, C cleared, + tagged."))), 78));

			foundAny = true;
		}

		if (foundAny)
		{
			return MarkupText.Empty;
		}

		await notifyService.Notify(executor, "MAIL: You have no matching mail in that mail folder.");
		return MarkupText.Plain("MAIL: You have no matching mail in that mail folder.");
	}

	private static string Counted(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

	/// <summary>A message's row: its number, the flags it has set, sender, subject and when it was sent.</summary>
	private static async ValueTask<ImmutableArray<Block>> Row(SharpMail mail, int index)
	{
		var flags = string.Concat(
			mail.Read ? "" : "N",
			mail.Urgent ? "U" : "",
			mail.Forwarded ? "F" : "",
			mail.Cleared ? "C" : "",
			mail.Tagged ? "+" : "");
		var from = (await mail.From.WithCancellation(CancellationToken.None)).Object()!.Name;
		return
		[
			MarkupText.Plain((index + 1).ToString(CultureInfo.InvariantCulture)),
			MarkupText.Plain(flags),
			MarkupText.Plain(from),
			mail.Subject,
			MarkupText.Plain(mail.DateSent.ToString("ddd MMM dd HH:mm", CultureInfo.InvariantCulture)),
		];
	}
}
