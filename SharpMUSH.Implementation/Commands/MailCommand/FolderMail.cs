using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Implementation.Commands.MailCommand;

/// <summary>
/// <c>@mail/folder</c>, <c>@mail/unfolder</c> and <c>@mail/file</c>. A folder is named by its number (0 to
/// <see cref="ExpandedMailData.MaxFolder"/>, 0 being INBOX) or by a name the player has given it, as
/// PennMUSH's <c>parse_folder</c> (<c>extmail.c:2839</c>) takes it.
/// </summary>
public static class FolderMail
{
	/// <summary><c>FOLDER_NAME_LEN</c> (<c>hdrs/extmail.h</c>): <c>BUFFER_LEN / 30</c>.</summary>
	private const int FolderNameLength = 8192 / 30;

	public static async ValueTask<MString> Handle(IMUSHCodeParser parser,
		IExpandedObjectDataService objectDataService,
		IMediator? mediator,
		INotifyService? notifyService,
		MString? arg0, MString? arg1, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator!);
		if (executor is not SharpPlayer executorPlayer)
		{
			throw new InvalidOperationException("@mail reads a player's own mail, and its dispatcher routes only players here.");
		}

		var folderInfo = await MailFolders.LoadAsync(objectDataService, executorPlayer);

		switch (switches)
		{
			case ["FOLDERS"] or ["FOLDER"] when arg0 is null || arg0.ToPlainText().Trim().Length == 0:
				return await GetMailFolderInfo(mediator!, notifyService!, executor, executorPlayer, folderInfo);

			case ["FOLDERS"] or ["FOLDER"] when folderInfo.Resolve(arg0.ToPlainText()) is not int:
				return await WhatFolder(notifyService!, executor);

			case ["FOLDERS"] or ["FOLDER"] when arg1 is null || arg1.ToPlainText().Length == 0:
				return await SetCurrentMailFolder(objectDataService, notifyService!, executor, executorPlayer,
					folderInfo.Resolve(arg0.ToPlainText()) is int current ? current : 0);

			case ["FOLDERS"] or ["FOLDER"]:
				return await RenameMailFolder(objectDataService, mediator!, notifyService!, executor, executorPlayer,
					folderInfo, folderInfo.Resolve(arg0.ToPlainText()) is int renamed ? renamed : 0, arg1!.ToPlainText());

			case ["UNFOLDER"] when arg0 is null || arg0.ToPlainText().Length == 0:
				await notifyService!.Notify(executor, "MAIL: You must specify a folder name or number");
				return MarkupText.Empty;

			case ["UNFOLDER"] when folderInfo.Resolve(arg0.ToPlainText()) is int unnamed:
				return await UnMailFolder(objectDataService, mediator!, notifyService!, executor, executorPlayer, unnamed);

			case ["UNFOLDER"]:
				return await WhatFolder(notifyService!, executor);

			case ["FILE"] when (arg0, arg1) is ({ } msgList, { } folder):
				return await MoveToMailFolder(parser, objectDataService, mediator!, notifyService!, msgList, executor,
					executorPlayer, folder);

			default:
				await notifyService!.Notify(executor, "Invalid arguments for @mail folder command.");
				return MarkupText.Plain(ErrorMessages.Returns.InvalidMailFolderArguments);
		}
	}

	private static async ValueTask<MString> WhatFolder(INotifyService notifyService, AnySharpObject executor)
	{
		await notifyService.Notify(executor, "MAIL: What folder is that?");
		return MarkupText.Empty;
	}

	/// <summary>
	/// <c>do_mail_file</c> (<c>extmail.c:616</c>): files each message, telling the player where it came from and
	/// where it went. Filing also uncleares a message. A name the player has not used yet becomes a new folder,
	/// as a MAILFILTER's answer does.
	/// </summary>
	private static async ValueTask<MString> MoveToMailFolder(IMUSHCodeParser parser,
		IExpandedObjectDataService objectDataService, IMediator mediator, INotifyService notifyService, MString msgList,
		AnySharpObject executor, SharpPlayer player, MString folder)
		=> await MessageListHelper.Handle(parser, objectDataService, mediator, notifyService, msgList, executor) switch
		{
			IAsyncEnumerable<SharpMail> list => await FileAsync(objectDataService, mediator, notifyService, list, executor,
				player, folder.ToPlainText(), msgList.ToPlainText().Trim().Equals("all", StringComparison.OrdinalIgnoreCase)),
			Error<string> error => await MessageListHelper.RefuseAsync(notifyService, executor, error.Value)
		};

	private static async ValueTask<MString> FileAsync(IExpandedObjectDataService objectDataService, IMediator mediator,
		INotifyService notifyService, IAsyncEnumerable<SharpMail> list, AnySharpObject executor, SharpPlayer player,
		string folderSpec, bool all)
	{
		if (await MailFolders.FileTargetAsync(objectDataService, player, folderSpec) is not MailFolder target)
		{
			await notifyService.Notify(executor, "MAIL: Invalid folder specification");
			return MarkupText.Empty;
		}

		var data = await MailFolders.LoadAsync(objectDataService, player);
		var messages = await MessageListHelper.WithPositionsAsync(mediator, player, data, list);
		if (messages.Count == 0)
		{
			await notifyService.Notify(executor, "MAIL: You don't have any matching messages!");
			return MarkupText.Empty;
		}

		foreach (var origin in messages)
		{
			var mail = origin.Mail;
			await mediator.Send(new MoveMailFolderCommand(mail, target.Name));
			if (mail.Cleared)
			{
				await mediator.Send(new UpdateMailCommand(mail, MailUpdate.ClearEdit(false)));
			}

			if (!all)
			{
				await notifyService.Notify(executor,
					$"MAIL: Msg {origin} filed in folder {target.Number} [{target.DisplayName}]");
			}
		}

		if (all)
		{
			await notifyService.Notify(executor, $"MAIL: All messages filed in folder {target.Number} [{target.DisplayName}]");
		}

		return MarkupText.Plain(target.Number.ToString());
	}

	/// <summary><c>do_mail_unfolder</c> (<c>extmail.c:358</c>): the folder keeps its number and its messages.</summary>
	private static async ValueTask<MString> UnMailFolder(IExpandedObjectDataService objectDataService, IMediator mediator,
		INotifyService notifyService, AnySharpObject executor, SharpPlayer player, int number)
	{
		// Folder 0 is INBOX, which has no other name to lose.
		if (number != 0)
		{
			await MailFolders.RenameAsync(objectDataService, mediator, player, number, null);
		}

		await notifyService.Notify(executor, $"MAIL: Folder {number} now has no name");
		return MarkupText.Empty;
	}

	/// <summary>
	/// <c>do_mail_change_folder</c> with a new name (<c>extmail.c:328-340</c>). The folder keeps its number; its
	/// messages are stored under the new name.
	/// </summary>
	private static async ValueTask<MString> RenameMailFolder(IExpandedObjectDataService objectDataService,
		IMediator mediator, INotifyService notifyService, AnySharpObject executor, SharpPlayer player,
		ExpandedMailData folderInfo, int number, string newName)
	{
		if (newName.Length > FolderNameLength)
		{
			await notifyService.Notify(executor, "MAIL: Folder name too long");
			return MarkupText.Empty;
		}

		// A name is alphanumeric (extmail.c:333). One that starts with a digit would read as a folder number, and
		// INBOX is folder 0's.
		if (!newName.All(char.IsAsciiLetterOrDigit) || newName.Length == 0 || char.IsAsciiDigit(newName[0]))
		{
			await notifyService.Notify(executor, "MAIL: Illegal folder name");
			return MarkupText.Empty;
		}

		if (number == 0)
		{
			await notifyService.Notify(executor, "MAIL: You cannot rename the INBOX folder.");
			return MarkupText.Plain(ErrorMessages.Returns.CannotRenameInbox);
		}

		// Messages are stored under their folder's name, so two folders cannot share one.
		if (folderInfo.Resolve(newName) is int holder && holder != number)
		{
			await notifyService.Notify(executor, $"MAIL: Folder {holder} is already named '{newName}'");
			return MarkupText.Empty;
		}

		await MailFolders.RenameAsync(objectDataService, mediator, player, number, newName);
		await notifyService.Notify(executor, $"MAIL: Folder {number} now named '{newName}'");
		return MarkupText.Empty;
	}

	private static async ValueTask<MString> SetCurrentMailFolder(IExpandedObjectDataService objectDataService,
		INotifyService notifyService, AnySharpObject executor, SharpPlayer player, int number)
	{
		var folder = await MailFolders.SetCurrentAsync(objectDataService, player, number);
		await notifyService.Notify(executor, $"MAIL: Current folder set to {folder.Number} [{folder.DisplayName}].");
		return MarkupText.Plain(folder.Number.ToString());
	}

	/// <summary>
	/// <c>do_mail_change_folder</c> with no folder (<c>extmail.c:313-321</c>): each folder that holds mail, highest
	/// number first, then the current folder.
	/// </summary>
	private static async ValueTask<MString> GetMailFolderInfo(IMediator mediator, INotifyService notifyService,
		AnySharpObject executor, SharpPlayer player, ExpandedMailData folderInfo)
	{
		for (var number = ExpandedMailData.MaxFolder; number >= 0; number--)
		{
			var folder = MailFolders.Folder(folderInfo, number);
			var mail = await mediator.CreateStream(new GetMailListQuery(player, folder.Name)).ToArrayAsync();
			if (mail.Length == 0)
			{
				continue;
			}

			var unread = mail.Count(x => !x.Cleared && !x.Read);
			var cleared = mail.Count(x => x.Cleared);
			await notifyService.Notify(executor,
				$"MAIL: {mail.Length} messages in folder {number} [{folder.DisplayName}] ({unread} unread, {cleared} cleared).");
		}

		var current = folderInfo.NumberOf(folderInfo.ActiveFolder ?? ExpandedMailData.Inbox) is int active ? active : 0;
		var currentFolder = MailFolders.Folder(folderInfo, current);
		await notifyService.Notify(executor,
			$"MAIL: Current folder is {current} [{currentFolder.DisplayName}].", executor);
		return MarkupText.Plain(current.ToString());
	}
}
