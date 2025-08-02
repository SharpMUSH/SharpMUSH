using System.Collections.Immutable;
using DotNext;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class FolderMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, MString? arg0, MString? arg1, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(parser.Mediator);
		var executorPlayer = executor.AsPlayer;
		
		// TODO: Consider that this is a duplicate call to the ObjectDataService.
		var folderInfo =
			await parser.ObjectDataService.GetExpandedDataAsync<ExpandedMailData>(executor.Object());

		switch (switches)
		{
			case ["FOLDER"] when (arg0, arg1) is (null, null):
				return await GetMailFolderInfo(parser, executor, executorPlayer);

			case ["FOLDER"] when (arg0, arg1) is ({ } folder, null):
				return await SetCurrentMailFolder(parser, folderInfo, folder, executor);

			case ["FOLDER"] when (arg0, arg1) is ({ } folder, { } newName):
				return await RenameMailFolder(parser, folder, executor, executorPlayer, newName, folderInfo);

			case ["UNFOLDER"] when (arg0, arg1) is ({ } folder, null):
				return await UnMailFolder(parser, executorPlayer, folder, executor, folderInfo);

			case ["FILE"] when (arg0, arg1) is ({ } msgList, { } folder):
				return await MoveToMailFolder(parser, msgList, executor, folder, folderInfo);

			default:
				await parser.NotifyService.Notify(executor, "Invalid arguments for @mail folder command.");
				return MModule.single("#-1 Invalid arguments for @mail folder command.");
		}
	}

	private static async Task<MString> MoveToMailFolder(IMUSHCodeParser parser, MString msgList,
		AnySharpObject executor, MString folder, ExpandedMailData? folderInfo)
	{
		var maybeList = await MessageListHelper.Handle(parser, msgList, executor);
		if (maybeList.IsError)
		{
			await parser.NotifyService.Notify(executor, maybeList.AsError);
			return MModule.single(maybeList.AsError);
		}

		var list = maybeList.AsMailList;
		foreach (var mail in list)
		{
			await parser.Mediator.Send(new MoveMailFolderCommand(mail, folder.ToPlainText()));
		}

		await parser.NotifyService.Notify(executor, $"MAIL: Moved {list.Length} messages to {folder.ToPlainText()}.");
		await parser.ObjectDataService.SetExpandedDataAsync(
			new ExpandedMailData(
				Folders: (folderInfo?.Folders ?? [])
				.ToImmutableHashSet()
				.Add(folder.ToPlainText())
				.ToArray()),
			executor.Object(), 
			ignoreNull: true);

		return folder;
	}

	private static async Task<MString> UnMailFolder(IMUSHCodeParser parser, SharpPlayer executorPlayer, MString folder,
		AnySharpObject executor, ExpandedMailData? folderInfo)
	{
		await parser.Mediator.Send(new RenameMailFolderCommand(executorPlayer, folder.ToPlainText(), "INBOX"));
		await parser.NotifyService.Notify(executor, $"MAIL: {folder.ToPlainText()} folder renamed to INBOX.");
		await parser.ObjectDataService.SetExpandedDataAsync(
			new ExpandedMailData(
				Folders: (folderInfo?.Folders ?? [])
				.ToImmutableArray()
				.Remove(folder.ToPlainText())
				.ToArray()),
			executor.Object(), 
			ignoreNull: true);
		return MModule.single("");
	}

	private static async Task<MString> RenameMailFolder(IMUSHCodeParser parser, MString folder,
		AnySharpObject executor, SharpPlayer executorPlayer, MString newName, ExpandedMailData? folderInfo)
	{
		if (folder.ToPlainText().Equals("INBOX", StringComparison.InvariantCultureIgnoreCase))
		{
			await parser.NotifyService.Notify(executor, "MAIL: You cannot rename the INBOX folder.");
			return MModule.single("#-1 You cannot rename the INBOX folder.");
		}

		await parser.Mediator.Send(new RenameMailFolderCommand(executorPlayer, folder.ToPlainText(), newName.ToPlainText()));
		await parser.NotifyService.Notify(executor,
			$"MAIL: {folder.ToPlainText()} folder renamed to {newName.ToPlainText()}.");
		await parser.ObjectDataService.SetExpandedDataAsync(
			new ExpandedMailData(
				Folders: (folderInfo?.Folders ?? [])
				.ToImmutableHashSet()
				.Remove(folder.ToPlainText()).Add(newName.ToPlainText())
				.ToArray()),
			executor.Object(), 
			ignoreNull: true);

		return MModule.single("");
	}

	private static async Task<MString> SetCurrentMailFolder(IMUSHCodeParser parser, ExpandedMailData? folderInfo, MString folder,
		AnySharpObject executor)
	{
		await parser.ObjectDataService.SetExpandedDataAsync(
			new ExpandedMailData(Folders: folderInfo?.Folders ?? [], ActiveFolder: folder.ToPlainText()),
			executor.Object());
		return MModule.single(folder.ToPlainText());
	}

	private static async Task<MString> GetMailFolderInfo(IMUSHCodeParser parser, AnySharpObject executor, SharpPlayer executorPlayer)
	{
		var currentFolder = await MessageListHelper.CurrentMailFolder(parser, executor);
		var folderMailList = (await parser.Mediator.Send(
				new GetMailListQuery(executorPlayer, currentFolder)))
			.ToImmutableArray();
		var unread = folderMailList.Count(x => !x.Read);
		var cleared = folderMailList.Count(x => x.Cleared);
		var totalMail = folderMailList.Length;

		await parser.NotifyService.Notify(executor,
			$"MAIL: {totalMail} messages in folder {currentFolder} ({unread} unread, {cleared} cleared).");
		await parser.NotifyService.Notify(executor,
			$"MAIL: Current folder is {currentFolder}.");
		return MModule.single(unread.ToString());
	}
}