using System.Collections.Immutable;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public class FolderMail
{
	/*
	 *  The MUSH mail system allows each player 16 folders, numbered from 0 to 15. Mail can only be in 1 folder at a time.
	 *  Folder 0 is the "inbox" where new mail is received. Most @mail commands operate on only the current folder.

  @mail/folder
        This commands lists all folders which contain mail, telling how many messages are in each, and what the current folder is.

  @mail/folder <folder#|foldername>
        This command sets your current folder to <folder#>.

  @mail/folder <folder#> = <foldername>
        This command gives <folder#> a name.

  @mail/unfolder <folder#|foldername>
        This command removes a folder's name

  @mail/file <msg-list>=<folder#>
        This command moves all messages in msg-list from the current folder to a new folder, <folder#>.

See also: @mailfilter
	 *
	 */

	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, MString? arg0, MString? arg1, string[] switches)
	{
		// Switches can be: FOLDER, UNFOLDER, or FILE.
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
					new ExpandedMailData(ActiveFolder: folder.ToPlainText()));
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