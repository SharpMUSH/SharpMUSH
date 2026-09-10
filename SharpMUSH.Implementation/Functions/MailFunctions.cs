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
	private async ValueTask<(string folder, int messageIndex)> ParseMessageSpec(
		IMUSHCodeParser parser,
		AnySharpObject player,
		string messageSpec)
	{
		var parts = messageSpec.Split(':', 2);
		string folder;
		int messageIndex;

		if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
		{
			folder = parts[0].Trim().ToUpper();
			if (!int.TryParse(parts[1].Trim(), out messageIndex) || messageIndex < 1)
			{
				return (folder, -1);
			}
		}
		else
		{
			folder = await MessageListHelper.CurrentMailFolder(parser, ObjectDataService, player);
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
	private async ValueTask<SharpMail?> GetMailMessage(
		AnySharpObject player,
		string folder,
		int messageIndex)
	{
		return await Mediator.Send(new GetMailQuery(player.AsPlayer, messageIndex, folder));
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
	private async ValueTask<PlayerMessageResult> ParsePlayerAndMessageArgs(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		Dictionary<string, CallState> args)
	{
		if (args.Count == 1)
		{
			return PlayerMessageResult.Success(executor, args["0"].Message!.ToPlainText());
		}

		if (!await executor.IsWizard())
		{
			return PlayerMessageResult.FromError(ErrorMessages.Returns.PermissionDenied);
		}

		var playerArg = args["0"].Message!.ToPlainText()!;
		var locateResult = await LocateService.LocateAndNotifyIfInvalid(
			parser, executor, executor, playerArg, LocateFlags.PlayersPreference);

		if (locateResult.IsError)
		{
			return PlayerMessageResult.FromError(locateResult.AsError.Value);
		}

		if (locateResult.IsNone)
		{
			return PlayerMessageResult.FromError(ErrorMessages.Returns.NoSuchPlayer);
		}

		return PlayerMessageResult.Success(locateResult.AsPlayer, args["1"].Message!.ToPlainText());
	}

	/// <summary>
	/// Helper to check if executor can view another player's mail (must be wizard)
	/// </summary>
	private async ValueTask<bool> CanViewOtherPlayerMail(AnySharpObject executor)
	{
		return await executor.IsWizard();
	}

	/// <summary>The counts the mail statistics functions report, taken in one pass over a mailbox.</summary>
	private readonly record struct MailTally(int Total, int Read, int Cleared, int Bytes)
	{
		public int Unread => Total - Read;
	}

	private static ValueTask<MailTally> TallyMail(IAsyncEnumerable<SharpMail> mail)
		=> mail.AggregateAsync(new MailTally(), (tally, m) => new MailTally(
			tally.Total + 1,
			tally.Read + (m.Read ? 1 : 0),
			tally.Cleared + (m.Cleared ? 1 : 0),
			tally.Bytes + m.Content.Length));

	[SharpFunction(Name = "mail", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["player", "status"])]
	public async ValueTask<CallState> mail(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (args.Count == 0 || (args.Count == 1 && string.IsNullOrWhiteSpace(args["0"].Message?.ToPlainText())))
		{
			var allMail = Mediator.CreateStream(new GetAllMailListQuery(executor.AsPlayer));
			var count = await allMail.CountAsync();
			return new CallState(count.ToString());
		}

		var arg0 = args["0"].Message!.ToPlainText();

		var isMsgNumber = IsMessageNumber(arg0);

		if (args.Count == 1 && !isMsgNumber)
		{
			if (!await CanViewOtherPlayerMail(executor))
			{
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, executor, executor, arg0, LocateFlags.PlayersPreference,
				async target =>
				{
					var tally = await TallyMail(Mediator.CreateStream(new GetAllMailListQuery(target.AsPlayer)));
					return new CallState($"{tally.Read} {tally.Unread} {tally.Cleared}");
				});
		}

		if (args.Count == 1)
		{
			var (folder, messageIndex) = await ParseMessageSpec(parser, executor, arg0);
			if (messageIndex < 0)
			{
				return new CallState(ErrorMessages.Returns.NoSuchMail);
			}

			var mail = await GetMailMessage(executor, folder, messageIndex);
			if (mail == null)
			{
				return new CallState(ErrorMessages.Returns.NoSuchMail);
			}

			return new CallState(mail.Content);
		}

		var arg1 = args["1"].Message!.ToPlainText();

		if (!await CanViewOtherPlayerMail(executor))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, arg0, LocateFlags.PlayersPreference,
			async target =>
			{
				var (folder, messageIndex) = await ParseMessageSpec(parser, target, arg1);
				if (messageIndex < 0)
				{
					return new CallState(ErrorMessages.Returns.NoSuchMail);
				}

				var mail = await GetMailMessage(target, folder, messageIndex);
				if (mail == null)
				{
					return new CallState(ErrorMessages.Returns.NoSuchMail);
				}

				return new CallState(mail.Content);
			});
	}

	/// <summary>
	/// Check if a string is a valid message number (e.g., "123" or "INBOX:5")
	/// </summary>
	private bool IsMessageNumber(string arg)
	{
		if (string.IsNullOrEmpty(arg))
		{
			return false;
		}

		// "123", or "FOLDER:123": a folder before the first colon, the number after it.
		var text = arg.AsSpan();
		Span<System.Range> parts = stackalloc System.Range[2];
		return text.Split(parts, ':') switch
		{
			1 => !text.ContainsAnyExceptInRange('0', '9'),
			_ => !text[parts[0]].IsWhiteSpace() && !text[parts[1]].ContainsAnyExceptInRange('0', '9')
		};
	}
	[SharpFunction(Name = "maillist", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["folder", "flags"])]
	public async ValueTask<CallState> maillist(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		AnySharpObject targetPlayer = executor;
		string? messageListSpec = null;

		if (args.Count == 1)
		{
			messageListSpec = args["0"].Message?.ToPlainText();
		}
		else if (args.Count == 2)
		{
			if (!await CanViewOtherPlayerMail(executor))
			{
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var playerArg = args["0"].Message!.ToPlainText()!;
			var locateResult = await LocateService.LocateAndNotifyIfInvalid(
				parser, executor, executor, playerArg, LocateFlags.PlayersPreference);

			if (locateResult.IsError || locateResult.IsNone)
			{
				return new CallState(ErrorMessages.Returns.NoSuchPlayer);
			}

			targetPlayer = locateResult.AsPlayer;
			messageListSpec = args["1"].Message?.ToPlainText();
		}

		var msgListArg = messageListSpec != null ? MarkupText.Plain(messageListSpec) : null;
		var filteredList = await MessageListHelper.Handle(
			parser, ObjectDataService, Mediator, NotifyService, msgListArg, targetPlayer);

		if (filteredList.IsError)
		{
			return new CallState("#-1 " + filteredList.AsError);
		}

		var mailList = filteredList.AsMailList;

		var results = new List<string>();
		await foreach (var mail in mailList)
		{
			// The message's 1-based position within its folder, which is how @mail names it.
			var position = await Mediator.CreateStream(new GetMailListQuery(targetPlayer.AsPlayer, mail.Folder))
				.Select((m, index) => (m.Id, Position: index + 1))
				.Where(x => x.Id == mail.Id)
				.Select(x => x.Position)
				.FirstOrDefaultAsync();

			if (position > 0)
			{
				results.Add($"{mail.Folder}:{position}");
			}
		}

		return new CallState(string.Join(" ", results));
	}
	[SharpFunction(Name = "mailfrom", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["message"])]
	public async ValueTask<CallState> mailfrom(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var parseResult = await ParsePlayerAndMessageArgs(parser, executor, args);
		if (parseResult.IsError)
		{
			return new CallState(parseResult.Error!);
		}

		var (folder, messageIndex) = await ParseMessageSpec(parser, parseResult.Player!, parseResult.MessageSpec!);
		if (messageIndex < 0)
		{
			return new CallState(ErrorMessages.Returns.NoSuchMail);
		}

		var mail = await GetMailMessage(parseResult.Player!, folder, messageIndex);
		if (mail == null)
		{
			return new CallState(ErrorMessages.Returns.NoSuchMail);
		}

		var from = await mail.From.WithCancellation(CancellationToken.None);
		return new CallState(from.Object()?.DBRef.ToString() ?? "#-1");
	}
	/// <summary>
	/// extmail.c:1466 — <c>do_mail_send(executor, args[0], args[1], 0, 1, 0)</c>: the same send
	/// <c>@mail</c> performs, with silent=1 and nosig=0.
	/// </summary>
	[SharpFunction(Name = "mailsend", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["player", "message"])]
	public async ValueTask<CallState> mailsend(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		await SendMail.Handle(parser, PermissionService, LocateService, ObjectDataService, Mediator,
			NotifyService, AttributeService, Configuration,
			args["0"].Message!, args["1"].Message!, ["SILENT"]);

		// do_mail_send notifies the sender about a bad recipient, so the function returns nothing.
		return new CallState(string.Empty);
	}
	[SharpFunction(Name = "mailstats", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["player"])]
	public async ValueTask<CallState> mailstats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var playerArg = args["0"].Message!.ToPlainText();

		AnySharpObject target;
		if (string.IsNullOrWhiteSpace(playerArg))
		{
			target = executor;
		}
		else
		{
			if (!await CanViewOtherPlayerMail(executor))
			{
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var locateResult = await LocateService.LocateAndNotifyIfInvalid(
				parser, executor, executor, playerArg, LocateFlags.PlayersPreference);

			if (locateResult.IsError || locateResult.IsNone)
			{
				return new CallState(ErrorMessages.Returns.NoSuchPlayer);
			}

			target = locateResult.AsPlayer;
		}

		var allSentMail = Mediator.CreateStream(new GetAllSentMailListQuery(target.Object()));
		var allReceivedMail = Mediator.CreateStream(new GetAllMailListQuery(target.AsPlayer));

		var sentCount = await allSentMail.CountAsync();
		var receivedCount = await allReceivedMail.CountAsync();

		return new CallState($"{sentCount} {receivedCount}");
	}
	[SharpFunction(Name = "maildstats", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["player"])]
	public async ValueTask<CallState> maildstats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var playerArg = args["0"].Message!.ToPlainText();

		AnySharpObject target;
		if (string.IsNullOrWhiteSpace(playerArg))
		{
			target = executor;
		}
		else
		{
			if (!await CanViewOtherPlayerMail(executor))
			{
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var locateResult = await LocateService.LocateAndNotifyIfInvalid(
				parser, executor, executor, playerArg, LocateFlags.PlayersPreference);

			if (locateResult.IsError || locateResult.IsNone)
			{
				return new CallState(ErrorMessages.Returns.NoSuchPlayer);
			}

			target = locateResult.AsPlayer;
		}

		var sent = await TallyMail(Mediator.CreateStream(new GetAllSentMailListQuery(target.Object())));
		var received = await TallyMail(Mediator.CreateStream(new GetAllMailListQuery(target.AsPlayer)));

		return new CallState($"{sent.Total} {sent.Unread} {sent.Cleared} {received.Total} {received.Unread} {received.Cleared}");
	}
	[SharpFunction(Name = "mailfstats", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["folder"])]
	public async ValueTask<CallState> mailfstats(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var playerArg = args["0"].Message!.ToPlainText();

		AnySharpObject target;
		if (string.IsNullOrWhiteSpace(playerArg))
		{
			target = executor;
		}
		else
		{
			if (!await CanViewOtherPlayerMail(executor))
			{
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var locateResult = await LocateService.LocateAndNotifyIfInvalid(
				parser, executor, executor, playerArg, LocateFlags.PlayersPreference);

			if (locateResult.IsError || locateResult.IsNone)
			{
				return new CallState(ErrorMessages.Returns.NoSuchPlayer);
			}

			target = locateResult.AsPlayer;
		}

		var sent = await TallyMail(Mediator.CreateStream(new GetAllSentMailListQuery(target.Object())));
		var received = await TallyMail(Mediator.CreateStream(new GetAllMailListQuery(target.AsPlayer)));

		return new CallState($"{sent.Total} {sent.Unread} {sent.Cleared} {sent.Bytes} {received.Total} {received.Unread} {received.Cleared} {received.Bytes}");
	}
	[SharpFunction(Name = "mailstatus", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["message"])]
	public async ValueTask<CallState> mailstatus(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var parseResult = await ParsePlayerAndMessageArgs(parser, executor, args);
		if (parseResult.IsError)
		{
			return new CallState(parseResult.Error!);
		}

		var (folder, messageIndex) = await ParseMessageSpec(parser, parseResult.Player!, parseResult.MessageSpec!);
		if (messageIndex < 0)
		{
			return new CallState(ErrorMessages.Returns.NoSuchMail);
		}

		var mail = await GetMailMessage(parseResult.Player!, folder, messageIndex);
		if (mail == null)
		{
			return new CallState(ErrorMessages.Returns.NoSuchMail);
		}

		// Format status as per @mail/list format: [NCUF+]
		var read = mail.Read ? "-" : "N";
		var cleared = mail.Cleared ? "C" : "-";
		var urgent = mail.Urgent ? "U" : "-";
		var forwarded = mail.Forwarded ? "F" : "-";
		var tagged = mail.Tagged ? "+" : "-";

		return new CallState($"{read}{cleared}{urgent}{forwarded}{tagged}");
	}
	[SharpFunction(Name = "mailsubject", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number"])]
	public async ValueTask<CallState> mailsubject(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var parseResult = await ParsePlayerAndMessageArgs(parser, executor, args);
		if (parseResult.IsError)
		{
			return new CallState(parseResult.Error!);
		}

		var (folder, messageIndex) = await ParseMessageSpec(parser, parseResult.Player!, parseResult.MessageSpec!);
		if (messageIndex < 0)
		{
			return new CallState(ErrorMessages.Returns.NoSuchMail);
		}

		var mail = await GetMailMessage(parseResult.Player!, folder, messageIndex);
		if (mail == null)
		{
			return new CallState(ErrorMessages.Returns.NoSuchMail);
		}

		return new CallState(mail.Subject);
	}
	[SharpFunction(Name = "mailtime", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["message"])]
	public async ValueTask<CallState> mailtime(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var parseResult = await ParsePlayerAndMessageArgs(parser, executor, args);
		if (parseResult.IsError)
		{
			return new CallState(parseResult.Error!);
		}

		var (folder, messageIndex) = await ParseMessageSpec(parser, parseResult.Player!, parseResult.MessageSpec!);
		if (messageIndex < 0)
		{
			return new CallState(ErrorMessages.Returns.NoSuchMail);
		}

		var mail = await GetMailMessage(parseResult.Player!, folder, messageIndex);
		if (mail == null)
		{
			return new CallState(ErrorMessages.Returns.NoSuchMail);
		}

		return new CallState(mail.DateSent.ToUnixTimeSeconds().ToString());
	}
	[SharpFunction(Name = "malias", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["alias"])]
	public async ValueTask<CallState> malias(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Mail aliases are not yet implemented in the system
		// Return empty result as per documentation behavior when no aliases exist
		await Task.CompletedTask;
		return new CallState(string.Empty);
	}
}