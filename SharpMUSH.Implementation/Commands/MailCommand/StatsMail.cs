using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using Errors = SharpMUSH.Library.Definitions.Errors;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class StatsMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, MString? arg0, string[] switches)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var target = executor;

		if (!string.IsNullOrEmpty(arg0?.ToPlainText()))
		{
			if (!(executor.IsGod() || executor.IsWizard()))
			{
				await parser.NotifyService.Notify(executor, Errors.ErrorPerm);
				return MModule.single(Errors.ErrorPerm);
			}

			var maybeTarget = await parser.LocateService.LocateAndNotifyIfInvalid(
				parser, executor, executor, arg0.ToPlainText(),
				LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference);

			if (maybeTarget.IsError)
			{
				return MModule.single(maybeTarget.AsError.Value);
			}

			if (maybeTarget.IsNone)
			{
				return MModule.single(Errors.ErrorCantSeeThat);
			}

			target = maybeTarget.AsPlayer;
		}

		switch (switches)
		{
			case ["CSTATS"]:
				var currentFolder = await MessageListHelper.CurrentMailFolder(parser, executor);
				var stats = (await parser.Mediator.Send(new GetMailListQuery(target.AsPlayer, currentFolder))).ToArray();
				await parser.NotifyService.Notify(executor, $"MAIL: {stats.Length} messages in folder [{currentFolder}] ({stats.Sum(x => x.Read ? 0 : 1)} unread, {stats.Sum(x => x.Cleared ? 1 : 0)} cleared).");
				return MModule.empty();
		}
		
		// TODO: Consider memory implications of loading them all using ToArray().
		var allSentMail = (await parser.Mediator.Send(new GetAllSentMailListQuery(target.Object()))).ToArray();
		var allReceivedMail = (await parser.Mediator.Send(new GetAllMailListQuery(target.AsPlayer))).ToArray();
		var targetName = target.Object().Name;
		
		switch (switches)
		{
			case ["STATS"]:
				await parser.NotifyService.Notify(executor, $"{targetName} sent {allSentMail.Length} messages.");
				await parser.NotifyService.Notify(executor, $"{targetName} received {allReceivedMail.Length} messages.");				
				return MModule.empty();

			case ["DSTATS"]:
				await parser.NotifyService.Notify(executor, $"Mail statistics for {targetName}:");
				await parser.NotifyService.Notify(executor, 
					$"{allSentMail.Length} messages sent, {allSentMail.Sum(x => x.Read ? 0 : 1)} unread, {allSentMail.Sum(x => x.Cleared ? 1 : 0)} cleared.");
				await parser.NotifyService.Notify(executor, 
					$"{allReceivedMail.Length} messages received, {allReceivedMail.Sum(x => x.Read ? 0 : 1)} unread, {allReceivedMail.Sum(x => x.Cleared ? 1 : 0)} cleared.");
				await parser.NotifyService.Notify(executor, $"Last is dated {allSentMail.Max(x => x.DateSent)}");
				return MModule.empty();

			case ["FSTATS"]:
				await parser.NotifyService.Notify(executor, $"Mail statistics for {targetName}:");
				var sentSize = allSentMail.Sum(x => x.Content.Length);
				var receivedSize = allReceivedMail.Sum(x => x.Content.Length);
				await parser.NotifyService.Notify(executor, 
					$"{allSentMail.Length} messages sent, {allSentMail.Sum(x => x.Read ? 0 : 1)} unread, {allSentMail.Sum(x => x.Cleared ? 1 : 0)} cleared, totalling {sentSize} characters.");
				await parser.NotifyService.Notify(executor, 
					$"{allReceivedMail.Length} messages received, {allReceivedMail.Sum(x => x.Read ? 0 : 1)} unread, {allReceivedMail.Sum(x => x.Cleared ? 1 : 0)} cleared, totalling {receivedSize} characters.");
				await parser.NotifyService.Notify(executor, $"Last is dated {allSentMail.Max(x => x.DateSent)}");
				return MModule.empty();

			default:
				return MModule.empty();
		}
	}
}