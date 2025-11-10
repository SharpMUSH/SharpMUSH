using System.Collections.Immutable;
using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class FolderMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, 
		IExpandedObjectDataService objectDataService, 
		IMediator? mediator, 
		INotifyService? notifyService, 
		MString? arg0, MString? arg1, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator!);
		var executorPlayer = executor.AsPlayer;
		
		var folderInfo =
			await objectDataService.GetExpandedDataAsync<ExpandedMailData>(executor.Object());

		switch (switches)
		{
			case ["FOLDER"] when (arg0, arg1) is (null, null):
				return await GetMailFolderInfo(parser, objectDataService, mediator, notifyService, executor, executorPlayer, folderInfo);

			case ["FOLDER"] when (arg0, arg1) is ({ } folder, null):
				return await SetCurrentMailFolder(parser, objectDataService, folderInfo, folder, executor);

			case ["FOLDER"] when (arg0, arg1) is ({ } folder, { } newName):
				return await RenameMailFolder(parser, objectDataService, mediator, notifyService, folder, executor, executorPlayer, newName, folderInfo);

			case ["UNFOLDER"] when (arg0, arg1) is ({ } folder, null):
				return await UnMailFolder(parser,objectDataService, mediator, notifyService, executorPlayer, folder, executor, folderInfo);

			case ["FILE"] when (arg0, arg1) is ({ } msgList, { } folder):
				return await MoveToMailFolder(parser, objectDataService, mediator, notifyService, msgList, executor, folder, folderInfo);

			default:
				await notifyService!.Notify(executor, "Invalid arguments for @mail folder command.");
				return MModule.single("#-1 Invalid arguments for @mail folder command.");
		}
	}

	private static async Task<MString> MoveToMailFolder(IMUSHCodeParser parser, IExpandedObjectDataService objectDataService, IMediator? mediator, INotifyService? notifyService, MString msgList,
		AnySharpObject executor, MString folder, ExpandedMailData? folderInfo)
	{
		var maybeList = await MessageListHelper.Handle(parser, objectDataService, mediator, notifyService, msgList, executor);
		if (maybeList.IsError)
		{
			await notifyService!.Notify(executor, maybeList.AsError);
			return MModule.single(maybeList.AsError);
		}

		var list = maybeList.AsMailList;
		var length = 0;
		await foreach (var mail in list)
		{
			length++;
			await mediator!.Send(new MoveMailFolderCommand(mail, folder.ToPlainText()));
		}

		await notifyService!.Notify(executor, $"MAIL: Moved {length} messages to {folder.ToPlainText()}.");
		await objectDataService.SetExpandedDataAsync(
			new ExpandedMailData(
				Folders: (folderInfo?.Folders ?? [])
				.ToImmutableHashSet()
				.Add(folder.ToPlainText())
				.ToArray()),
			executor.Object(), 
			ignoreNull: true);

		return folder;
	}

	private static async Task<MString> UnMailFolder(IMUSHCodeParser parser, IExpandedObjectDataService objectDataService, IMediator? mediator, INotifyService? notifyService,  SharpPlayer executorPlayer, MString folder,
		AnySharpObject executor, ExpandedMailData? folderInfo)
	{
		await mediator!.Send(new RenameMailFolderCommand(executorPlayer, folder.ToPlainText(), "INBOX"));
		await notifyService!.Notify(executor, $"MAIL: {folder.ToPlainText()} folder renamed to INBOX.");
		await objectDataService.SetExpandedDataAsync(
			new ExpandedMailData(
				Folders: (folderInfo?.Folders ?? [])
				.ToImmutableArray()
				.Remove(folder.ToPlainText())
				.ToArray()),
			executor.Object(), 
			ignoreNull: true);
		return MModule.single("");
	}

	private static async Task<MString> RenameMailFolder(IMUSHCodeParser parser, IExpandedObjectDataService objectDataService, IMediator? mediator, INotifyService? notifyService, MString folder,
		AnySharpObject executor, SharpPlayer executorPlayer, MString newName, ExpandedMailData? folderInfo)
	{
		if (folder.ToPlainText().Equals("INBOX", StringComparison.InvariantCultureIgnoreCase))
		{
			await notifyService!.Notify(executor, "MAIL: You cannot rename the INBOX folder.");
			return MModule.single("#-1 You cannot rename the INBOX folder.");
		}

		await mediator!.Send(new RenameMailFolderCommand(executorPlayer, folder.ToPlainText(), newName.ToPlainText()));
		await notifyService!.Notify(executor,
			$"MAIL: {folder.ToPlainText()} folder renamed to {newName.ToPlainText()}.");
		await objectDataService.SetExpandedDataAsync(
			new ExpandedMailData(
				Folders: (folderInfo?.Folders ?? [])
				.ToImmutableHashSet()
				.Remove(folder.ToPlainText()).Add(newName.ToPlainText())
				.ToArray()),
			executor.Object(), 
			ignoreNull: true);

		return MModule.single("");
	}

	private static async Task<MString> SetCurrentMailFolder(IMUSHCodeParser parser, IExpandedObjectDataService objectDataService, ExpandedMailData? folderInfo, MString folder,
		AnySharpObject executor)
	{
		await objectDataService.SetExpandedDataAsync(
			new ExpandedMailData(Folders: folderInfo?.Folders ?? [], ActiveFolder: folder.ToPlainText()),
			executor.Object());
		return MModule.single(folder.ToPlainText());
	}

	private static async Task<MString> GetMailFolderInfo(IMUSHCodeParser parser, IExpandedObjectDataService objectDataService, IMediator? mediator, INotifyService? notifyService, AnySharpObject executor, SharpPlayer executorPlayer, ExpandedMailData? folderInfo)
	{
		var currentFolder = folderInfo?.ActiveFolder ?? "INBOX";
		var folderMail = mediator!.CreateStream(
			new GetMailListQuery(executorPlayer, currentFolder));
		var folderMailList = await folderMail.ToArrayAsync();
		var unread = folderMailList.Count(x => !x.Read);
		var cleared = folderMailList.Count(x => x.Cleared);
		var totalMail = folderMailList.Length;

		await notifyService!.Notify(executor,
			$"MAIL: {totalMail} messages in folder {currentFolder} ({unread} unread, {cleared} cleared).");
		await notifyService.Notify(executor,
			$"MAIL: Current folder is {currentFolder}.");
		return MModule.single(unread.ToString());
	}
}