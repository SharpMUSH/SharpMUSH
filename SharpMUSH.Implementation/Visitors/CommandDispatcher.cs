using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Visitors;

/// <summary>
/// Decides what a command is — PennMUSH's <c>process_command</c> and <c>command_parse</c> ladder:
/// the halted-executor gate, <c>SOCKET</c> commands, speech tokens, chat aliases, single-token
/// commands, exits, built-ins by prefix, standard attributes, <c>$</c>-commands scope by scope, and
/// finally <c>HUH_COMMAND</c> — and hands it to <see cref="CommandInvocationPipeline"/> to run.
/// </summary>
/// <remarks>Stateless: one instance serves every evaluation of a parser.</remarks>
internal sealed class CommandDispatcher(EvaluationServices services)
{
	/// <summary>
	/// Evaluates the command, with the parser info given.
	/// </summary>
	/// <remarks>
	/// Call State is expected to be empty on return.
	/// But if one wanted to implement a @pipe command that can pass a result from say, a @dig command, 
	/// there would be a need for some way of passing on secondary data.
	/// </remarks>
	/// <param name="visitor">The visitor walking the command: its parser, options and logger.</param>
	/// <param name="src">Original string</param>
	/// <param name="context">Command Context</param>
	/// <returns>An empty Call State</returns>
	public async ValueTask<Option<CallState>> DispatchAsync(SharpMUSHParserVisitor visitor, MString src,
		ICommandContext context)
	{
		// Every command leaves a %>. A built-in records its own as its CommandOutput declares; anything that
		// recorded nothing — a command with no output, a $-command, a refusal before the command ran —
		// leaves the #-1 error it failed with, or nothing.
		var commandText = visitor.Parser.CurrentState.CommandText;
		var outputVersion = commandText?.OutputVersion;
		var result = await DispatchCommandAsync(visitor, src, context);
		if (commandText is not null && commandText.OutputVersion == outputVersion)
		{
			commandText.SetOutput(result is CallState { Message: { } message } && message.ToPlainText().StartsWith("#-1", StringComparison.Ordinal)
				? message
				: MarkupText.Empty);
		}

		return result;
	}

	/// <summary>
	/// PennMUSH's ladder, one rung per stage: the login screen, speech tokens, chat aliases,
	/// single-token commands, exits, built-ins and standard attributes, and then whatever the
	/// evaluated line matches — a <c>$</c>-command, a lock's failure message, or <c>HUH_COMMAND</c>.
	/// </summary>
	private async ValueTask<Option<CallState>> DispatchCommandAsync(SharpMUSHParserVisitor visitor, MString src,
		ICommandContext context)
	{
		var parser = visitor.Parser;

		// Hoisted out of the try so the catch can name the command that failed.
		string? command = null;

		try
		{
			var firstCommandMatch = context.evaluationString();
			command = CommandName(firstCommandMatch);
			if (command is null)
				return new None();

			var commandText = CommandSlice(src, firstCommandMatch);
			parser.CurrentState.CommandText?.Begin(commandText);
			RecordConnectionActivity(parser, command);

			if (await RefuseHaltedExecutor(parser)) return new None();

			if (TryFindSocketCommand(parser, command, out var socketCommand))
				return await services.Commands.SocketAsync(visitor, parser, src, context, command, socketCommand);

			if (parser.CurrentState.Executor is null && parser.CurrentState.Handle is not null)
			{
				await services.NotifyService.NotifyLocalized(parser.CurrentState.Handle.Value,
					nameof(ErrorMessages.Notifications.NoSuchCommandAtLogin));
				return new None();
			}

			var tokenText = WithoutLeadingSpaces(commandText);
			if (SpeechTokenCommand(tokenText) is { } speechReplacer)
				return await RedispatchSpeechAsync(parser, speechReplacer, tokenText);

			if (command[..1] == visitor.Configuration.CurrentValue.Chat.ChatTokenAlias.ToString())
			{
				switch (await MatchChatAliasAsync(parser, command[1..]))
				{
					case SharpChannel channel when !context.evaluationString().IsEmpty:
						return await services.Commands.ChannelAsync(parser, channel, context, src);
					case AmbiguousChatAlias:
						return new None();
				}
			}

			if (TryFindSingleTokenCommand(parser, command, out var singleTokenCommand))
				return await services.Commands.SingleTokenAsync(visitor, parser, src, context, command, tokenText,
					singleTokenCommand);

			var executorObject = await parser.CurrentState.KnownExecutorObject(services.Mediator);
			if (executorObject.IsContent)
			{
				var locate = await services.LocateService.Locate(
					parser,
					executorObject,
					executorObject,
					command,
					LocateFlags.ExitsInTheRoomOfLooker
					| LocateFlags.EnglishStyleMatching
					| LocateFlags.ExitsPreference
					| LocateFlags.OnlyMatchTypePreference);

				if (locate is AnySharpObject and SharpExit exit)
				{
					return await CommandInvocationPipeline.GoAsync(parser, exit, command);
				}
			}

			// Step 4: Check if we are setting an attribute: &... -- we're just treating this as a Single Token Command for now.
			// Who would rely on a room alias being & anyway?
			// Step 5: Check @COMMAND in command library
			var builtIn = FindBuiltIn(parser, command);
			if (builtIn.Definition is CommandDefinition definition)
			{
				return await services.Commands.InternalAsync(visitor, parser, src, context, builtIn.RootCommand,
					builtIn.Switches, definition);
			}

			// Step 6: Check @attribute setting
			// Standard attributes (e.g., DESCRIBE) can be set using @attrname object=value syntax
			// This supports prefix matching when the attribute has the "prefixmatch" flag
			if (builtIn.RootCommand.StartsWith('@') && context.evaluationString() != null)
			{
				var attrCommandResult = await services.StandardAttributes.TryRunAsync(parser, src, context, builtIn.RootCommand);
				if (attrCommandResult.IsSome())
				{
					return attrCommandResult;
				}
			}

			// Step 7: Enter Aliases
			// Step 8: Leave Aliases
			return await DispatchUnmatchedAsync(visitor, src, commandText, executorObject);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			// A command that throws used to be logged server-side and then swallowed to
			// CallState.Empty, which is indistinguishable to the player from a command that simply
			// had nothing to say. Surface it instead, following FunctionInvocationPipeline's precedent.
			var correlationId = ExceptionReport.NewCorrelationId();
			visitor.Logger.LogError(ex, "{Method} threw for command {Command} (correlation {CorrelationId})",
				"EvaluateCommands", command ?? "(unknown)", correlationId);

			return await ReportCommandException(parser, visitor.Logger, ex, command, correlationId);
		}
	}

	/// <summary>
	/// The command's first word, or <see langword="null"/> when there is none: no evaluation string, or
	/// a command body that began with only whitespace.
	/// </summary>
	private static string? CommandName(IEvaluationStringContext? firstCommandMatch)
	{
		if (firstCommandMatch?.SourceInterval.Length is null or 0)
			return null;

		var command = firstCommandMatch.GetText().TrimStart();

		var spaceIndex = command.AsSpan().IndexOf(' ');
		if (spaceIndex != -1)
		{
			command = command[..spaceIndex];
		}

		return command.Length == 0 ? null : command;
	}

	/// <summary>
	/// Per-command, markup-preserving slice of this command out of the (possibly whole-list) src.
	/// In a ';' command-list, src is the entire list (e.g. "alpha;beta"); each command is addressed
	/// by its evaluationString span. Built-in commands already re-slice src this exact way in
	/// CommandArgumentSplitter.SplitAsync; $command matching must use the same slice (commandText) rather than the whole
	/// src, otherwise a $command in a list is matched against the entire list and its ^...$ pattern
	/// never matches. This is the same arithmetic as SplitAsync's realSubtext.
	/// </summary>
	private static MString CommandSlice(MString src, IEvaluationStringContext firstCommandMatch)
		=> src.Substring(firstCommandMatch.Start.StartIndex, firstCommandMatch.Stop.StopIndex - firstCommandMatch.Start.StartIndex + 1);

	/// <summary>Stamps the connection's last signal and command count; <c>IDLE</c> counts as neither.</summary>
	private void RecordConnectionActivity(IMUSHCodeParser parser, string command)
	{
		if (parser.CurrentState.Handle is null || command == "IDLE")
			return;

		services.ConnectionService.Update(parser.CurrentState.Handle.Value, "LastConnectionSignal",
			DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
		services.ConnectionService.IncrementMetadata(parser.CurrentState.Handle.Value, "CommandCount");
	}

	/// <summary>
	/// The <c>SOCKET</c> command a connection's line names: an exact name at any time, and while the
	/// connection has not logged in, an unambiguous prefix or the connect screen's <c>DOING</c> and
	/// <c>SESSION</c>.
	/// </summary>
	private static bool TryFindSocketCommand(IMUSHCodeParser parser, string command, out CommandDefinition definition)
	{
		definition = default;
		if (parser.CurrentState.Handle is null)
			return false;

		// The library is keyed case-insensitively, so an exact-name match is one lookup. Scanning
		// every registered command for it - twice, here and for the single-token check below - was
		// a sixth of all bytes a plain `think` allocated.
		if (parser.CommandLibrary.TryGetValue(command, out var socketCandidate)
				&& socketCandidate.IsSystem
				&& socketCandidate.LibraryInformation.Attribute.Behavior.HasFlag(CommandBehavior.SOCKET))
		{
			definition = socketCandidate.LibraryInformation;
			return true;
		}

		return parser.CurrentState.Executor is null
			&& (TryFindLoginPrefix(parser, command, out definition) || TryFindLoginListing(parser, command, out definition));
	}

	/// <summary>
	/// PennMUSH-style unambiguous prefix abbreviation for pre-login SOCKET commands
	/// (e.g. "con"/"co"/"conn" -> CONNECT). Only kicks in when there was no exact match
	/// above, and only while the connection has not logged in yet. If the typed token is
	/// a prefix of more than one system SOCKET command name, it's ambiguous and we fall
	/// through to the same "no such command" handling as an unknown command.
	/// </summary>
	private static bool TryFindLoginPrefix(IMUSHCodeParser parser, string command, out CommandDefinition definition)
	{
		var socketPrefixMatches = parser.CommandLibrary.Where(x
			=> x.Value.IsSystem
				 && x.Value.LibraryInformation.Attribute.Behavior.HasFlag(CommandBehavior.SOCKET)
				 && x.Key.StartsWith(command, StringComparison.CurrentCultureIgnoreCase)).ToList();

		definition = socketPrefixMatches.Count == 1 ? socketPrefixMatches[0].Value.LibraryInformation : default;
		return socketPrefixMatches.Count == 1;
	}

	/// <summary>
	/// PennMUSH src/bsd.c do_command(): at the connect screen WHO, DOING and SESSION are the same
	/// command — all three fall into dump_users(). They diverge only once a player is connected,
	/// where DOING and SESSION are ordinary in-game commands with their own output. WHO already
	/// carries CommandBehavior.SOCKET and answers anonymously, so the login-screen forms of the
	/// other two are routed to it rather than duplicated. DOING and SESSION deliberately keep
	/// CB.Default: giving them the SOCKET flag would drop them out of the in-game abbreviation
	/// trie, so "doin" would stop working for a logged-in player.
	/// PennMUSH matches these with strncmp, not equality, so "DOINGfoo" is DOING with a listing
	/// filter of "foo" rather than an unknown command.
	/// </summary>
	private static bool TryFindLoginListing(IMUSHCodeParser parser, string command, out CommandDefinition definition)
	{
		definition = default;
		if ((command.StartsWith("DOING", StringComparison.OrdinalIgnoreCase)
					|| command.StartsWith("SESSION", StringComparison.OrdinalIgnoreCase))
				&& parser.CommandLibrary.TryGetValue("WHO", out var who)
				&& who.IsSystem)
		{
			definition = who.LibraryInformation;
			return true;
		}

		return false;
	}

	/// <summary>
	/// command_parse runs `while (*p == ' ') p++` before its speech-token switch, so the token still
	/// counts when the player typed spaces in front of it: `  "hello` is a SAY. commandText is the raw
	/// slice and still carries those spaces (the command name was TrimStart()ed, commandText was
	/// not), so the token test and the re-dispatched remainder both work off this one value, and the
	/// slice can never be taken from a different offset than the test.
	/// </summary>
	private static MString WithoutLeadingSpaces(MString commandText)
	{
		var tokenStart = CommandArgumentSplitter.SkipSpaces(commandText, 0);
		return tokenStart > 0
			? commandText.Substring(tokenStart, commandText.Length - tokenStart)
			: commandText;
	}

	/// <summary>
	/// PennMUSH src/command.c command_parse(): before any command-table lookup, a leading
	/// SAY_TOKEN ("), POSE_TOKEN (:), SEMI_POSE_TOKEN (;) or EMIT_TOKEN (\) is replaced by the
	/// corresponding command name and the token character is skipped. Two details of that
	/// branch matter and are reproduced here:
	///   * ';' followed by a space means POSE, not SEMIPOSE (`; waves` -> `One waves`).
	///   * `parse_switches = 0` for every replacer, so `"/noeval x` says "/noeval x" rather
	///     than invoking SAY with a NOEVAL switch.
	/// Re-dispatching the rewritten line (rather than calling the command directly) keeps the
	/// token forms on exactly the same path as the spelled-out commands, including @hook.
	/// </summary>
	private static async ValueTask<Option<CallState>> RedispatchSpeechAsync(IMUSHCodeParser parser,
		string speechReplacer, MString tokenText)
	{
		// PennMUSH swaps the token for the command name without touching cmd_raw: %c stays `"hi`.
		var typed = parser.CurrentState.CommandText;
		typed?.KeepRawThroughRedispatch();
		try
		{
			var result = await parser.CommandParse(MarkupText.Concat(
				MarkupText.Plain(speechReplacer + " "), tokenText.Substring(1)));
			return result.HadErrors ? result : CallState.Empty;
		}
		finally
		{
			typed?.EndRedispatch();
		}
	}

	/// <summary>A chat alias that named more than one channel; the player has been told which.</summary>
	private readonly record struct AmbiguousChatAlias;

	/// <summary>What a chat alias (<c>+pub</c>) names: one channel, nothing, or several.</summary>
	private union ChatAliasMatch(SharpChannel, NotFound, AmbiguousChatAlias);

	/// <summary>
	/// The channel <paramref name="check"/> names: the one exact match, else the one prefix match. More
	/// than one of either is ambiguous, and the connection is told so.
	/// </summary>
	private async ValueTask<ChatAliasMatch> MatchChatAliasAsync(IMUSHCodeParser parser, string check)
	{
		var channels = services.Mediator.CreateStream(new GetChannelListQuery());

		var exactMatches = new List<SharpChannel>();
		var partialMatches = new List<SharpChannel>();

		await foreach (var ch in channels)
		{
			var channelName = ch.Name.ToPlainText();
			if (channelName.Equals(check, StringComparison.CurrentCultureIgnoreCase))
			{
				exactMatches.Add(ch);
			}
			else if (channelName.StartsWith(check, StringComparison.CurrentCultureIgnoreCase))
			{
				partialMatches.Add(ch);
			}
		}

		switch (exactMatches.Count, partialMatches.Count)
		{
			case (1, _):
				return exactMatches[0];
			case (0, 1):
				return partialMatches[0];
			case ( > 1, _):
				await NotifyHandle(parser, nameof(ErrorMessages.Notifications.AmbiguousChannelNameFormat), check);
				return new AmbiguousChatAlias();
			case (_, > 1):
				await NotifyHandle(parser, nameof(ErrorMessages.Notifications.AmbiguousChannelNameMatchesFormat), check,
					string.Join(", ", partialMatches.Select(c => c.Name.ToPlainText())));
				return new AmbiguousChatAlias();
			default:
				return new NotFound();
		}
	}

	private async ValueTask NotifyHandle(IMUSHCodeParser parser, string key, params object[] args)
	{
		if (parser.CurrentState.Handle is not null)
		{
			await services.NotifyService.NotifyLocalized(parser.CurrentState.Handle.Value, key, args);
		}
	}

	/// <summary>A single-token command (<c>&amp;</c>, <c>]</c>, ...) the line's first character names.</summary>
	private static bool TryFindSingleTokenCommand(IMUSHCodeParser parser, string command, out CommandDefinition definition)
	{
		var found = parser.CommandLibrary.TryGetAlternateValue(command.AsSpan(0, 1), out var singleTokenCandidate)
			&& singleTokenCandidate.IsSystem
			&& singleTokenCandidate.LibraryInformation.Attribute.Behavior.HasFlag(CommandBehavior.SingleToken);
		definition = found ? singleTokenCandidate.LibraryInformation : default;
		return found;
	}

	/// <summary>
	/// The command name split into its root and switches, and the built-in the root abbreviates, when
	/// there is one.
	/// </summary>
	private readonly record struct BuiltInLookup(string RootCommand, string[] Switches, CommandDefinition? Definition);

	private static BuiltInLookup FindBuiltIn(IMUSHCodeParser parser, string command)
	{
		// Use CommandTrie for efficient prefix matching instead of LINQ
		var slashIndex = command.AsSpan().IndexOf('/');
		var rootCommand =
			command[..(slashIndex > -1 ? slashIndex : command.Length)];
		var switches = slashIndex > -1
			? command[slashIndex..].Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			: [];

		var matchResult = rootCommand.Equals("HUH_COMMAND", StringComparison.CurrentCultureIgnoreCase)
			? null
			: CommandTrie.For(parser.CommandLibrary).FindShortestMatch(rootCommand);

		// If no match found and rootCommand contains '=', try matching just the part before '='
		// This handles cases like "addcom=Public" where the command name and args have no space separator.
		if (matchResult == null)
		{
			var equalsIndex = rootCommand.IndexOf('=');
			if (equalsIndex > 0)
			{
				var commandPart = rootCommand[..equalsIndex];
				matchResult = CommandTrie.For(parser.CommandLibrary).FindShortestMatch(commandPart);
				if (matchResult != null)
				{
					rootCommand = commandPart;
				}
			}
		}

		return new BuiltInLookup(rootCommand, switches, matchResult?.Definition);
	}

	/// <summary>
	/// A line no built-in claimed: its <c>$</c>-command, else the failure messages of the locks that
	/// refused one, else <c>HUH_COMMAND</c>. An error in evaluating the line is kept on whichever
	/// answers.
	/// </summary>
	private async ValueTask<Option<CallState>> DispatchUnmatchedAsync(SharpMUSHParserVisitor visitor, MString src,
		MString commandText, AnySharpObject executorObject)
	{
		var parser = visitor.Parser;

		// Step 9: User Defined Commands nearby
		// -- This is going to be a very important place to Cache the commands.
		// A caching strategy is going to be reliant on the Attribute Service.
		// Optimistic that the command still exists, until we try and it no longer does?
		// What's the best way to retrieve the Regex or Wildcard pattern and transform it?
		// It needs to take an area to search in. So this is definitely its own service.
		// PennMUSH matches $-commands against the command line AFTER evaluation (game.c tests the
		// evaluated cptr), so substitutions and functions in the typed line are applied before the
		// pattern is checked and before its wildcards capture %0... This mirrors what the hook
		// OVERRIDE/EXTEND path already does. It is only reached once no built-in command matched
		// (Steps 1-8 above), so a built-in never pays for this evaluation.
		var evaluatedCommandResult = await parser.FunctionParse(commandText);
		var evaluatedCommandText = evaluatedCommandResult?.Message ?? commandText;
		// game.c records this line as %u before looking for a $-command, so the caller keeps it
		// whether a $-command, HUH_COMMAND or its hook ends up handling the command.
		parser.CurrentState.CommandText?.Evaluated = evaluatedCommandText;

		// Objects whose @lock/command or @lock/use refused a match are process_command's errdblist.
		var lockFailures = new List<AnySharpObject>();
		var result = await RunUserDefinedCommandAsync(visitor, evaluatedCommandText, executorObject, lockFailures) switch
		{
			Option<CallState> ran => ran,
			NotFound when await ReportLockFailuresAsync(parser, executorObject, lockFailures) => CallState.Empty,
			NotFound => await RunHuhCommandAsync(parser, src)
		};

		if (evaluatedCommandResult?.HadErrors != true) return result;
		return (result is CallState value ? value : CallState.Empty) with { HadErrors = true };
	}

	/// <summary>
	/// Steps 9 and 11-15: $-commands nearby, then on the location's zone master room, the location
	/// itself, the executor's personal zone master room, and the master room and its contents.
	/// The first scope with a match runs it; a scope is only looked up once those before it failed.
	/// </summary>
	private async ValueTask<Found<Option<CallState>>> RunUserDefinedCommandAsync(SharpMUSHParserVisitor visitor,
		MString evaluatedCommandText, AnySharpObject executorObject, List<AnySharpObject> lockFailures)
	{
		var parser = visitor.Parser;

		// Only a command typed at a connection runs its $-command in place (QUEUE_INPLACE).
		var inPlace = parser.CurrentState.Flags.HasFlag(ParserStateFlags.DirectInput)
			&& !parser.CurrentState.Flags.HasFlag(ParserStateFlags.QueueMatches);

		// Live discovery uses the invoking executor's perception before handlers can match.
		// Explicit configured hooks keep their separate administrative dispatch path.
		var reality = services.Reality;
		Func<DBRef, CancellationToken, ValueTask<bool>> perceive = reality is IRealityObservationProvider observations
			? await observations.ObserveAsync(executorObject.Object().DBRef, ExecutionBudget.CurrentToken)
			: (target, ct) => reality.CanPerceiveAsync(executorObject.Object().DBRef, target, ct);
		IAsyncEnumerable<AnySharpObject> PerceivedCandidates(IAsyncEnumerable<AnySharpObject> candidates)
			=> candidates.Where((candidate, _) => perceive(candidate.Object().DBRef, ExecutionBudget.CurrentToken));

		for (var scope = CommandScope.Nearby; scope <= CommandScope.MasterRoom; scope++)
		{
			if (await CandidatesIn(scope, executorObject, visitor.Configuration)
					is not IAsyncEnumerable<AnySharpObject> candidates)
			{
				continue;
			}

			var userDefinedCommandMatches = await services.CommandDiscoveryService.MatchUserDefinedCommand(
				parser,
				PerceivedCandidates(candidates),
				evaluatedCommandText,
				executorObject,
				lockFailures);

			if (userDefinedCommandMatches.TryGetValue(out var matches))
			{
				return await services.Commands.UserDefinedAsync(parser, matches, inPlace);
			}
		}

		return new NotFound();
	}

	/// <summary>
	/// process_command (src/game.c:1366-1372): a command nothing ran first gives each object whose lock
	/// refused it its COMMAND_LOCK`FAILURE triad (fail_commands, src/game.c:2777-2790), and is a Huh?
	/// only when none of them had one. errdb_grow stops the list at 50 (src/game.c:2794-2797).
	/// </summary>
	/// <returns>Whether any of them had a message.</returns>
	private async ValueTask<bool> ReportLockFailuresAsync(IMUSHCodeParser parser, AnySharpObject executorObject,
		List<AnySharpObject> lockFailures)
	{
		if (lockFailures.Count == 0)
			return false;

		var didIt = services.Provider.GetRequiredService<IDidItService>();
		var anyMessage = false;
		foreach (var refused in lockFailures.Take(50))
		{
			anyMessage |= await didIt.FailLock(parser, executorObject, refused, LockType.Command);
		}

		return anyMessage;
	}

	/// <summary>
	/// Step 16: HUH_COMMAND is run — its OVERRIDE hook when one is set and answers, the built-in otherwise.
	/// </summary>
	private async ValueTask<Option<CallState>> RunHuhCommandAsync(IMUSHCodeParser parser, MString src)
	{
		// Check for HUH_COMMAND hook before running the built-in HUH_COMMAND
		var huhHook = await services.HookService.GetHookAsync("HUH_COMMAND", "OVERRIDE");
		if (huhHook is CommandHook huhOverride)
		{
			var executor = await parser.CurrentState.ExecutorObject(services.Mediator);
			// Construct the full command input for $-command matching
			Option<MString> huhInput = src;
			var huhResult = await services.Commands.HookAsync(parser, executor, huhOverride, huhInput);
			if (huhResult.IsSome())
			{
				return huhResult;
			}
		}

		// The name is synthetic; %c and %u stay the line that matched nothing.
		var newParser = parser.Push(parser.CurrentState with
		{
			Command = "HUH_COMMAND",
			Arguments = [],
			Function = null
		});

		return await parser.CommandLibrary["HUH_COMMAND"].LibraryInformation.Command.Invoke(newParser);
	}

	/// <summary>Where a <c>$</c>-command is looked for, in the order PennMUSH looks.</summary>
	private enum CommandScope
	{
		/// <summary>Step 9: the executor, its location's contents and its own contents.</summary>
		Nearby,

		/// <summary>Step 11: the contents of the zone master room of the executor's location.</summary>
		LocationZone,

		/// <summary>Step 12: the executor's location itself.</summary>
		Location,

		/// <summary>Step 13: the contents of the executor's personal zone master room.</summary>
		PersonalZone,

		/// <summary>Steps 14-15: the master room and its contents.</summary>
		MasterRoom
	}

	/// <summary>
	/// The objects <paramref name="scope"/> searches for <paramref name="executor"/>'s <c>$</c>-commands,
	/// or <see cref="NotFound"/> when the scope does not apply: no location for something that is not
	/// content, no zone master room, or no master room.
	/// </summary>
	private async ValueTask<Found<IAsyncEnumerable<AnySharpObject>>> CandidatesIn(CommandScope scope,
		AnySharpObject executor, IOptionsWrapper<SharpMUSHOptions> configuration)
	{
		switch (scope)
		{
			case CommandScope.Nearby:
				return Found(NearbyObjects.ForAsync(services.Mediator, executor));

			case CommandScope.LocationZone when executor.IsContent:
				{
					// Step 10: Zone Exit Name and Aliases - handled in LocateService
					var executorLocation = await executor.AsContent.Location();
					var locationZone =
						await executorLocation.WithExitOption().Object().Zone.WithCancellation(CancellationToken.None);

					// If the location has a zone that is a room (ZMR), check for $-commands in ZMR contents
					if (locationZone is AnySharpObject and SharpRoom zoneMasterRoom)
					{
						AnySharpContainer zmr = zoneMasterRoom;
						return Found(zmr
							.Content(services.Mediator)
							.Select(x => x.WithRoomOption()));
					}

					return new NotFound();
				}

			case CommandScope.Location when executor.IsContent:
				{
					AnySharpObject[] item = [(await executor.AsContent.Location()).WithExitOption()];
					return Found(item.ToAsyncEnumerable());
				}

			case CommandScope.PersonalZone:
				{
					var executorZone = await executor.Object().Zone.WithCancellation(CancellationToken.None);
					if (executorZone is AnySharpObject and SharpRoom personalZoneRoom)
					{
						// If player has a ZMR as their personal zone, check for $-commands in ZMR contents
						AnySharpContainer personalZMR = personalZoneRoom;
						return Found(personalZMR
							.Content(services.Mediator)
							.Select(x => x.WithRoomOption()));
					}

					return new NotFound();
				}

			case CommandScope.MasterRoom:
				{
					var goConfig = configuration.CurrentValue.Database.MasterRoom;
					// A master room that does not exist has no global commands to offer.
					if (await services.Mediator.Send(new GetObjectNodeQuery(new DBRef(Convert.ToInt32(goConfig)))) is AnySharpObject globalObject)
					{
						AnySharpObject[] globalObjects = [globalObject];
						var globalObjectContent = globalObject.AsContainer
							.Content(services.Mediator)
							.Select(x => x.WithRoomOption());

						return Found(globalObjects.ToAsyncEnumerable().Union(globalObjectContent));
					}

					return new NotFound();
				}

			default:
				return new NotFound();
		}

		static Found<IAsyncEnumerable<AnySharpObject>> Found(IAsyncEnumerable<AnySharpObject> candidates) => candidates;
	}

	/// <summary>
	/// PennMUSH's <c>process_command</c> gate (<c>src/game.c:1181</c>): a halted executor runs no
	/// command, and its owner is told <c>Attempt to execute command by halted object #N</c> every
	/// time one is refused. A <em>player</em> is exempt only for a command it typed at a connection
	/// (<c>QUEUE_SOCKET</c>), which here is <see cref="ParserStateFlags.DirectInput"/> without
	/// <see cref="ParserStateFlags.QueueMatches"/> — the same pair the <c>QUEUE_INPLACE</c> decision
	/// reads, since <c>TEACH</c>'s lesson keeps the former's <c>QUEUE_NOLIST</c> meaning and not the
	/// socket's (<c>do_teach</c> queues it with neither flag, <c>src/speech.c:130-163</c>).
	/// </summary>
	/// <remarks>
	/// This is the far end of the queue's player exemption. <c>insert_que</c> (<c>src/cque.c:530</c>)
	/// and <c>do_entry</c> (<c>:1136</c>) drop a halted <em>object's</em> entry outright but admit a
	/// halted player's, so a halted player's queued work reaches execution and is refused here
	/// instead — noisily, once per command, rather than silently at admission. That is also what
	/// makes the flag safe to set on a runaway player (<c>pay_queue</c>, <c>:303-313</c>): the player
	/// keeps typing, and only what the queue carries for them stops.
	/// <para>Placed before <em>everything</em> the command name is looked up in, as
	/// <c>process_command</c> is — so a halted executor's <c>SOCKET</c> command, speech token, chat
	/// alias and <c>$</c>-command are all refused. The <c>SOCKET</c> branch below tests
	/// <see cref="ParserState.Handle"/>, and a queue entry keeps the handle it was started from, so
	/// letting it run first would answer a queued <c>WHO</c> that Penn refuses. The connection-level
	/// commands <c>do_command</c> (<c>src/bsd.c:4155-4269</c>) answers before <c>process_command</c>
	/// is reached are the typed ones, and those carry the exemption.</para>
	/// </remarks>
	private async ValueTask<bool> RefuseHaltedExecutor(IMUSHCodeParser parser)
	{
		if (parser.CurrentState.Executor is null) return false;
		if (await parser.CurrentState.ExecutorObject(services.Mediator) is not AnySharpObject executor) return false;
		if (executor.IsPlayer
			&& parser.CurrentState.Flags.HasFlag(ParserStateFlags.DirectInput)
			&& !parser.CurrentState.Flags.HasFlag(ParserStateFlags.QueueMatches)) return false;
		if (!await executor.HasFlag("HALT", ExecutionBudget.CurrentToken)) return false;

		var owner = (await executor.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef;
		await services.NotifyService.NotifyLocalized(owner,
			nameof(ErrorMessages.Notifications.HaltedObjectCommandRefusedFormat),
			executor.Object().DBRef.Number);
		return true;
	}

	/// <summary>
	/// Turns an escaped command exception into the player-visible <c>#-1 EXCEPTION: {json}</c>,
	/// notifies whoever ran the command, and returns it so nested evaluations see it too.
	/// </summary>
	/// <remarks>
	/// Notification targets, in order: the executor when there is one; otherwise the raw connection
	/// handle, which is the only recipient available at the connect screen (where a pre-login command
	/// such as <c>WHO</c> has no executor at all — precisely one of the cases this exists for).
	/// The whole body is defensive: this runs because something already failed, so a second failure
	/// here (a downed database behind <c>ExecutorObject</c>, say) must not escape and replace the
	/// original exception.
	/// </remarks>
	private async ValueTask<Option<CallState>> ReportCommandException(IMUSHCodeParser parser, ILogger logger,
		Exception ex, string? command, string correlationId)
	{
		try
		{
			var executor = await parser.CurrentState.ExecutorObject(services.Mediator) is AnySharpObject found ? found : null;
			var privileged = executor is not null && await executor.IsPriv();
			var message = ExceptionReport.Format(ex, command, correlationId, privileged);

			if (executor is not null)
			{
				await services.NotifyService.Notify(executor, message);
			}
			else if (parser.CurrentState.Handle is not null)
			{
				await services.NotifyService.Notify(parser.CurrentState.Handle.Value, message);
			}

			return new CallState(message) { HadErrors = true };
		}
		catch (Exception reportingFailure)
		{
			logger.LogError(reportingFailure,
				"Failed to report command exception (correlation {CorrelationId})", correlationId);

			// Still hand back an unprivileged payload: the player learns the command failed and gets
			// the id that reaches the log, even though the notification could not be delivered.
			return new CallState(ExceptionReport.Format(ex, command, correlationId, privileged: false)) { HadErrors = true };
		}
	}

	/// <summary>
	/// The command a leading speech token stands for, or <see langword="null"/> when the line does not
	/// begin with one. Mirrors the <c>switch (*p)</c> in PennMUSH's <c>command_parse</c>
	/// (src/command.c), including its special case that <c>';'</c> followed by a space is POSE.
	/// </summary>
	private static string? SpeechTokenCommand(MString commandText)
	{
		var text = commandText.ToPlainText();
		if (text.Length == 0)
		{
			return null;
		}

		return text[0] switch
		{
			'"' => "SAY",
			':' => "POSE",
			';' => text.Length > 1 && text[1] == ' ' ? "POSE" : "SEMIPOSE",
			'\\' => "@EMIT",
			_ => null
		};
	}
}
