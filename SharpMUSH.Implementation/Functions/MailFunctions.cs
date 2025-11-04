using OneOf;
using OneOf.Types;
using SharpMUSH.Implementation.Commands.MailCommand;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// Parse message specification (e.g. "123" or "INBOX:5") into folder and message index
	/// </summary>
	private static async ValueTask<(string folder, int messageIndex)> ParseMessageSpec(
		IMUSHCodeParser parser,
		AnySharpObject player,
		string messageSpec)
	{
		var parts = messageSpec.Split(':', 2);
		string folder;
		int messageIndex;

		if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
		{
			// Format: "folder:number"
			folder = parts[0].Trim().ToUpper();
			if (!int.TryParse(parts[1].Trim(), out messageIndex) || messageIndex < 1)
			{
				return (folder, -1);
			}
		}
		else
		{
			// Format: just "number" - use current folder
			folder = await MessageListHelper.CurrentMailFolder(parser, ObjectDataService!, player);
			if (!int.TryParse(messageSpec.Trim(), out messageIndex) || messageIndex < 1)
			{
				return (folder, -1);
			}
		}

		// Convert from 1-indexed to 0-indexed
		return (folder, messageIndex - 1);
	}

	/// <summary>
	/// Retrieve mail message by folder and index
	/// </summary>
	private static async ValueTask<SharpMail?> GetMailMessage(
		AnySharpObject player,
		string folder,
		int messageIndex)
	{
		return await Mediator!.Send(new GetMailQuery(player.AsPlayer, messageIndex, folder));
	}

	/// <summary>
	/// Result of parsing player and message arguments
	/// </summary>
	private class PlayerMessageResult
	{
		public bool IsError { get; init; }
		public string? Error { get; init; }
		public AnySharpObject? Player { get; init; }
		public string? MessageSpec { get; init; }

		public static PlayerMessageResult Success(AnySharpObject player, string messageSpec)
			=> new() { IsError = false, Player = player, MessageSpec = messageSpec };

		public static PlayerMessageResult FromError(string error)
			=> new() { IsError = true, Error = error };
	}

	/// <summary>
	/// Helper to parse target player and message spec from function arguments.
	/// Uses same methodology as commands - returns proper error types.
	/// </summary>
	private static async ValueTask<PlayerMessageResult> ParsePlayerAndMessageArgs(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		Dictionary<string, CallState> args)
	{
		// Single argument - query self
		if (args.Count == 1)
		{
			return PlayerMessageResult.Success(executor, args["0"].Message!.ToPlainText()!);
		}

		// Two arguments - must be wizard to view other player's mail
		if (!executor.IsGod() && !await executor.IsWizard())
		{
			return PlayerMessageResult.FromError("#-1 PERMISSION DENIED");
		}

		var playerArg = args["0"].Message!.ToPlainText()!;
		var locateResult = await LocateService!.LocateAndNotifyIfInvalid(
			parser, executor, executor, playerArg, LocateFlags.PlayersPreference);

		if (locateResult.IsError)
		{
			return PlayerMessageResult.FromError(locateResult.AsError.Value);
		}

		if (locateResult.IsNone)
		{
			return PlayerMessageResult.FromError("#-1 NO SUCH PLAYER");
		}

		return PlayerMessageResult.Success(locateResult.AsPlayer, args["1"].Message!.ToPlainText()!);
	}

	/// <summary>
	/// Helper to check if executor can view another player's mail (must be wizard)
	/// </summary>
	private static async ValueTask<bool> CanViewOtherPlayerMail(AnySharpObject executor)
	{
		return executor.IsGod() || await executor.IsWizard();
	}

	[SharpFunction(Name = "mail", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> mail(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// Case 1: mail() - return total count of messages in all folders
		if (args.Count == 0 || (args.Count == 1 && string.IsNullOrWhiteSpace(args["0"].Message?.ToPlainText())))
		{
			var allMail = Mediator!.CreateStream(new GetAllMailListQuery(executor.AsPlayer));
			var count = await allMail.CountAsync();
			return new CallState(count.ToString());
		}

		var arg0 = args["0"].Message!.ToPlainText()!;

		// Check if arg0 is a message number (contains only digits or folder:digits format)
		var isMsgNumber = IsMessageNumber(arg0);

		// Case 2: mail(player) - return "read unread cleared" counts for player
		if (args.Count == 1 && !isMsgNumber)
		{
			if (!await CanViewOtherPlayerMail(executor))
			{
				return new CallState("#-1 PERMISSION DENIED");
			}

			return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, executor, executor, arg0, LocateFlags.PlayersPreference,
				async target =>
				{
					var allMail = Mediator!.CreateStream(new GetAllMailListQuery(target.AsPlayer));
					var mailArray = await allMail.ToArrayAsync();
					var read = mailArray.Count(m => m.Read);
					var unread = mailArray.Count(m => !m.Read);
					var cleared = mailArray.Count(m => m.Cleared);
					return new CallState($"{read} {unread} {cleared}");
				});
		}

		// Case 3: mail(msg#) or mail(folder:msg#) - return text content of message
		if (args.Count == 1)
		{
			var (folder, messageIndex) = await ParseMessageSpec(parser, executor, arg0);
			if (messageIndex < 0)
			{
				return new CallState("#-1 NO SUCH MAIL");
			}

			var mail = await GetMailMessage(executor, folder, messageIndex);
			if (mail == null)
			{
				return new CallState("#-1 NO SUCH MAIL");
			}

			return new CallState(mail.Content.ToString());
		}

		// Case 4: mail(player, msg#) or mail(player, folder:msg#) - return text of player's message
		var arg1 = args["1"].Message!.ToPlainText()!;
		
		if (!await CanViewOtherPlayerMail(executor))
		{
			return new CallState("#-1 PERMISSION DENIED");
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, arg0, LocateFlags.PlayersPreference,
			async target =>
			{
				var (folder, messageIndex) = await ParseMessageSpec(parser, target, arg1);
				if (messageIndex < 0)
				{
					return new CallState("#-1 NO SUCH MAIL");
				}

				var mail = await GetMailMessage(target, folder, messageIndex);
				if (mail == null)
				{
					return new CallState("#-1 NO SUCH MAIL");
				}

				return new CallState(mail.Content.ToString());
			});
	}

	/// <summary>
	/// Check if a string is a valid message number (e.g., "123" or "INBOX:5")
	/// </summary>
	private static bool IsMessageNumber(string arg)
	{
		if (string.IsNullOrEmpty(arg))
		{
			return false;
		}

		if (arg.All(char.IsDigit))
		{
			return true;
		}

		if (arg.Contains(':'))
		{
			var parts = arg.Split(':', 2);
			return parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && parts[1].All(char.IsDigit);
		}

		return false;
	}
	[SharpFunction(Name = "maillist", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> maillist(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		AnySharpObject targetPlayer = executor;
		string? messageListSpec = null;

		// Parse arguments - can be (msglist) or (player, msglist)
		if (args.Count == 1)
		{
			messageListSpec = args["0"].Message?.ToPlainText();
		}
		else if (args.Count == 2)
		{
			// Must be wizard to view other player's mail
			if (!await CanViewOtherPlayerMail(executor))
			{
				return new CallState("#-1 PERMISSION DENIED");
			}

			var playerArg = args["0"].Message!.ToPlainText()!;
			var locateResult = await LocateService!.LocateAndNotifyIfInvalid(
				parser, executor, executor, playerArg, LocateFlags.PlayersPreference);
			
			if (locateResult.IsError || locateResult.IsNone)
			{
				return new CallState("#-1 NO SUCH PLAYER");
			}

			targetPlayer = locateResult.AsPlayer;
			messageListSpec = args["1"].Message?.ToPlainText();
		}

		// Use MessageListHelper to filter mail
		var msgListArg = messageListSpec != null ? MModule.single(messageListSpec) : null;
		var filteredList = await MessageListHelper.Handle(
			parser, ObjectDataService!, Mediator, NotifyService, msgListArg, targetPlayer);

		if (filteredList.IsError)
		{
			return new CallState("#-1 " + filteredList.AsError);
		}

		var mailList = filteredList.AsMailList;
		
		// Build result list by finding each mail's index in its folder using async enumeration
		var results = new List<string>();
		await foreach (var mail in mailList)
		{
			var folderMail = Mediator!.CreateStream(new GetMailListQuery(targetPlayer.AsPlayer, mail.Folder));
			var index = 0;
			await foreach (var m in folderMail)
			{
				if (m.Id == mail.Id)
				{
					results.Add($"{mail.Folder}:{index + 1}");
					break;
				}
				index++;
			}
		}

		return new CallState(string.Join(" ", results));
	}
	[SharpFunction(Name = "mailfrom", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> mailfrom(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		var parseResult = await ParsePlayerAndMessageArgs(parser, executor, args);
		if (parseResult.IsError)
		{
			return new CallState(parseResult.Error!);
		}

		var (folder, messageIndex) = await ParseMessageSpec(parser, parseResult.Player!, parseResult.MessageSpec!);
		if (messageIndex < 0)
		{
			return new CallState("#-1 NO SUCH MAIL");
		}

		var mail = await GetMailMessage(parseResult.Player!, folder, messageIndex);
		if (mail == null)
		{
			return new CallState("#-1 NO SUCH MAIL");
		}

		var from = await mail.From.WithCancellation(CancellationToken.None);
		return new CallState(from.Object()?.DBRef.ToString() ?? "#-1");
	}
	[SharpFunction(Name = "mailsend", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> mailsend(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}

		var args = parser.CurrentState.Arguments;
		var sender = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		var recipientArg = args["0"].Message!.ToPlainText()!;
		var messageArg = args["1"].Message!;

		// Locate recipient
		var locateResult = await LocateService!.LocateAndNotifyIfInvalid(
			parser, sender, sender, recipientArg, LocateFlags.PlayersPreference);
		
		if (locateResult.IsError)
		{
			return new CallState(locateResult.AsError.Value);
		}
		
		if (locateResult.IsNone)
		{
			return new CallState("#-1 NO SUCH PLAYER");
		}

		var recipient = locateResult.AsPlayer;

		// Check mail lock
		if (!PermissionService!.PassesLock(sender, recipient, LockType.Mail))
		{
			return new CallState("#-1 RECIPIENT DOES NOT ACCEPT MAIL FROM YOU");
		}

		// Parse subject and message (split on /)
		var subjectBodySplit = MModule.indexOf(messageArg, MModule.single("/"));
		
		var subject = subjectBodySplit > -1 
			? MModule.substring(0, subjectBodySplit, messageArg) 
			: MModule.substring(0, Math.Min(20, messageArg.Length), messageArg);
		
		var message = subjectBodySplit > -1
			? MModule.substring(subjectBodySplit + 1, messageArg.Length - subjectBodySplit - 1, messageArg) 
			: messageArg;

		// Create mail object
		var mail = new SharpMail
		{
			DateSent = DateTimeOffset.UtcNow,
			Fresh = true,
			Read = false,
			Tagged = false,
			Urgent = false,
			Cleared = false,
			Forwarded = false,
			Folder = "INBOX",
			Content = message,
			Subject = subject,
			From = new DotNext.Threading.AsyncLazy<AnyOptionalSharpObject>(async _ =>
			{
				return await ValueTask.FromResult(sender.WithNoneOption());
			}),
		};

		// Send the mail
		await Mediator!.Send(new Library.Commands.Database.SendMailCommand(sender.Object(), recipient, mail));

		return new CallState(string.Empty);
	}
	[SharpFunction(Name = "mailstats", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> mailstats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		var playerArg = args["0"].Message!.ToPlainText()!;
		
		// Check if querying self or another player
		AnySharpObject target;
		if (string.IsNullOrWhiteSpace(playerArg))
		{
			target = executor;
		}
		else
		{
			// Must be wizard to view other player's mail
			if (!await CanViewOtherPlayerMail(executor))
			{
				return new CallState("#-1 PERMISSION DENIED");
			}

			var locateResult = await LocateService!.LocateAndNotifyIfInvalid(
				parser, executor, executor, playerArg, LocateFlags.PlayersPreference);
			
			if (locateResult.IsError || locateResult.IsNone)
			{
				return new CallState("#-1 NO SUCH PLAYER");
			}

			target = locateResult.AsPlayer;
		}

		var allSentMail = Mediator!.CreateStream(new GetAllSentMailListQuery(target.Object()));
		var allReceivedMail = Mediator!.CreateStream(new GetAllMailListQuery(target.AsPlayer));

		var sentCount = await allSentMail.CountAsync();
		var receivedCount = await allReceivedMail.CountAsync();

		return new CallState($"{sentCount} {receivedCount}");
	}
	[SharpFunction(Name = "maildstats", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> maildstats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		var playerArg = args["0"].Message!.ToPlainText()!;
		
		// Check if querying self or another player
		AnySharpObject target;
		if (string.IsNullOrWhiteSpace(playerArg))
		{
			target = executor;
		}
		else
		{
			// Must be wizard to view other player's mail
			if (!await CanViewOtherPlayerMail(executor))
			{
				return new CallState("#-1 PERMISSION DENIED");
			}

			var locateResult = await LocateService!.LocateAndNotifyIfInvalid(
				parser, executor, executor, playerArg, LocateFlags.PlayersPreference);
			
			if (locateResult.IsError || locateResult.IsNone)
			{
				return new CallState("#-1 NO SUCH PLAYER");
			}

			target = locateResult.AsPlayer;
		}

		var allSentMail = await (Mediator!.CreateStream(new GetAllSentMailListQuery(target.Object()))).ToArrayAsync();
		var allReceivedMail = await (Mediator!.CreateStream(new GetAllMailListQuery(target.AsPlayer))).ToArrayAsync();

		var sentCount = allSentMail.Length;
		var sentUnread = allSentMail.Count(m => !m.Read);
		var sentCleared = allSentMail.Count(m => m.Cleared);
		
		var receivedCount = allReceivedMail.Length;
		var receivedUnread = allReceivedMail.Count(m => !m.Read);
		var receivedCleared = allReceivedMail.Count(m => m.Cleared);

		return new CallState($"{sentCount} {sentUnread} {sentCleared} {receivedCount} {receivedUnread} {receivedCleared}");
	}
	[SharpFunction(Name = "mailfstats", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> mailfstats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		var playerArg = args["0"].Message!.ToPlainText()!;
		
		// Check if querying self or another player
		AnySharpObject target;
		if (string.IsNullOrWhiteSpace(playerArg))
		{
			target = executor;
		}
		else
		{
			// Must be wizard to view other player's mail
			if (!await CanViewOtherPlayerMail(executor))
			{
				return new CallState("#-1 PERMISSION DENIED");
			}

			var locateResult = await LocateService!.LocateAndNotifyIfInvalid(
				parser, executor, executor, playerArg, LocateFlags.PlayersPreference);
			
			if (locateResult.IsError || locateResult.IsNone)
			{
				return new CallState("#-1 NO SUCH PLAYER");
			}

			target = locateResult.AsPlayer;
		}

		var allSentMail = await (Mediator!.CreateStream(new GetAllSentMailListQuery(target.Object()))).ToArrayAsync();
		var allReceivedMail = await (Mediator!.CreateStream(new GetAllMailListQuery(target.AsPlayer))).ToArrayAsync();

		var sentCount = allSentMail.Length;
		var sentUnread = allSentMail.Count(m => !m.Read);
		var sentCleared = allSentMail.Count(m => m.Cleared);
		var sentBytes = allSentMail.Sum(m => m.Content.Length);
		
		var receivedCount = allReceivedMail.Length;
		var receivedUnread = allReceivedMail.Count(m => !m.Read);
		var receivedCleared = allReceivedMail.Count(m => m.Cleared);
		var receivedBytes = allReceivedMail.Sum(m => m.Content.Length);

		return new CallState($"{sentCount} {sentUnread} {sentCleared} {sentBytes} {receivedCount} {receivedUnread} {receivedCleared} {receivedBytes}");
	}
	[SharpFunction(Name = "mailstatus", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> mailstatus(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		var parseResult = await ParsePlayerAndMessageArgs(parser, executor, args);
		if (parseResult.IsError)
		{
			return new CallState(parseResult.Error!);
		}

		var (folder, messageIndex) = await ParseMessageSpec(parser, parseResult.Player!, parseResult.MessageSpec!);
		if (messageIndex < 0)
		{
			return new CallState("#-1 NO SUCH MAIL");
		}

		var mail = await GetMailMessage(parseResult.Player!, folder, messageIndex);
		if (mail == null)
		{
			return new CallState("#-1 NO SUCH MAIL");
		}

		// Format status as per @mail/list format: [NCUF+]
		var read = mail.Read ? "-" : "N";
		var cleared = mail.Cleared ? "C" : "-";
		var urgent = mail.Urgent ? "U" : "-";
		var forwarded = mail.Forwarded ? "F" : "-";
		var tagged = mail.Tagged ? "+" : "-";

		return new CallState($"{read}{cleared}{urgent}{forwarded}{tagged}");
	}
	[SharpFunction(Name = "mailsubject", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> mailsubject(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		var parseResult = await ParsePlayerAndMessageArgs(parser, executor, args);
		if (parseResult.IsError)
		{
			return new CallState(parseResult.Error!);
		}

		var (folder, messageIndex) = await ParseMessageSpec(parser, parseResult.Player!, parseResult.MessageSpec!);
		if (messageIndex < 0)
		{
			return new CallState("#-1 NO SUCH MAIL");
		}

		var mail = await GetMailMessage(parseResult.Player!, folder, messageIndex);
		if (mail == null)
		{
			return new CallState("#-1 NO SUCH MAIL");
		}

		return new CallState(mail.Subject.ToString());
	}
	[SharpFunction(Name = "mailtime", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> mailtime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		var parseResult = await ParsePlayerAndMessageArgs(parser, executor, args);
		if (parseResult.IsError)
		{
			return new CallState(parseResult.Error!);
		}

		var (folder, messageIndex) = await ParseMessageSpec(parser, parseResult.Player!, parseResult.MessageSpec!);
		if (messageIndex < 0)
		{
			return new CallState("#-1 NO SUCH MAIL");
		}

		var mail = await GetMailMessage(parseResult.Player!, folder, messageIndex);
		if (mail == null)
		{
			return new CallState("#-1 NO SUCH MAIL");
		}

		return new CallState(mail.DateSent.ToUnixTimeSeconds().ToString());
	}
	[SharpFunction(Name = "malias", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> malias(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Mail aliases are not yet implemented in the system
		// Return empty result as per documentation behavior when no aliases exist
		await Task.CompletedTask;
		return new CallState(string.Empty);
	}
}