using System.Collections.Immutable;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public class FolderMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, MString? arg0, MString? arg1, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();

		switch (switches)
		{
			case ["FOLDER"] when (arg0, arg1) is (null, null) && executor.IsPlayer:
				var currentFolder = await MessageListHelper.CurrentMailFolder(parser, executor);
				var folderMailList = (await parser.Mediator.Send(
						new GetMailListQuery(executor.AsPlayer, currentFolder)))
					.ToImmutableArray();
				var unread = folderMailList.Count(x => !x.Read);
				var cleared = folderMailList.Count(x => x.Cleared);
				var totalMail = folderMailList.Length;

				await parser.NotifyService.Notify(executor,
					$"MAIL: {totalMail} messages in folder {currentFolder} ({unread} unread, {cleared} cleared).");
				await parser.NotifyService.Notify(executor,
					$"MAIL: Current folder is {currentFolder}.");
				return MModule.single(unread.ToString());

			case ["FOLDER"] when (arg0, arg1) is ({ } folder, null):
				await parser.ObjectDataService.SetExpandedDataAsync(executor.Object(), typeof(ExpandedMailData),
					new ExpandedMailData(Folders: [], ActiveFolder: folder.ToPlainText()));
				break;

			case ["FOLDER"] when (arg0, arg1) is ({ } folder, { } newName):
				//         This command gives <folder#> a name. 
				// TODO: Consider making 'mail folder' a Vertex type.
				// TODO: Consider a better command
				break;

			case ["UNFOLDER"] when (arg0, arg1) is (null, null):
				//         This command removes a folder's name
				// TODO: Consider making 'mail folder' a Vertex type.
				// TODO: Consider a better command
				break;

			case ["FILE"] when (arg0, arg1) is ({ } msgList, { } folder):
				//   @mail/file <msg-list>=<folder#>
				// This command moves all messages in msg-list from the current folder to a new folder, <folder#>.
				var maybeList = await MessageListHelper.Handle(parser, msgList, executor);
				if (maybeList.IsError)
				{
					await parser.NotifyService.Notify(executor, maybeList.AsError);
					return MModule.single(maybeList.AsError);
				}

				var list = maybeList.AsMailList;
				// TODO: Move each to the correct folder.
				return folder;

			default:
				await parser.NotifyService.Notify(executor, "Invalid arguments for @mail folder command.");
				return MModule.single("#-1 Invalid arguments for @mail folder command.");
		}

		return MModule.single("	OK");
	}
}