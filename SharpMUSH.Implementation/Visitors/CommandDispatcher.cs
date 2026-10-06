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
		CommandContext context)
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

	private async ValueTask<Option<CallState>> DispatchCommandAsync(SharpMUSHParserVisitor visitor, MString src,
		CommandContext context)
	{
		var parser = visitor.Parser;

		// Hoisted out of the try so the catch can name the command that failed.
		string? command = null;

		try
		{
			var firstCommandMatch = context.evaluationString();

			if (firstCommandMatch?.SourceInterval.Length is null or 0)
				return new None();

			command = firstCommandMatch.GetText().TrimStart();

			var spaceIndex = command.AsSpan().IndexOf(' ');
			if (spaceIndex != -1)
			{
				command = command[..spaceIndex];
			}

			// Guard: empty command name (e.g., from a command body that began with only whitespace).
			if (command.Length == 0)
				return new None();

			// Per-command, markup-preserving slice of this command out of the (possibly whole-list) src.
			// In a ';' command-list, src is the entire list (e.g. "alpha;beta"); each command is addressed
			// by its evaluationString span. Built-in commands already re-slice src this exact way in
			// CommandArgumentSplitter.SplitAsync; $command matching must use the same slice (commandText) rather than the whole
			// src, otherwise a $command in a list is matched against the entire list and its ^...$ pattern
			// never matches. This is the same arithmetic as SplitAsync's realSubtext.
			var commandText = src.Substring(firstCommandMatch.Start.StartIndex, firstCommandMatch.Stop.StopIndex - firstCommandMatch.Start.StartIndex + 1);
			parser.CurrentState.CommandText?.Begin(commandText);

			if (parser.CurrentState.Handle is not null && command != "IDLE")
			{
				services.ConnectionService.Update(parser.CurrentState.Handle.Value, "LastConnectionSignal",
					DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
				services.ConnectionService.IncrementMetadata(parser.CurrentState.Handle.Value, "CommandCount");
			}

			if (await RefuseHaltedExecutor(parser)) return new None();

			// The library is keyed case-insensitively, so an exact-name match is one lookup. Scanning
			// every registered command for it - twice, here and for the single-token check below - was
			// a sixth of all bytes a plain `think` allocated.
			if (parser.CurrentState.Handle is not null
					&& parser.CommandLibrary.TryGetValue(command, out var socketCandidate)
					&& socketCandidate.IsSystem
					&& socketCandidate.LibraryInformation.Attribute.Behavior.HasFlag(CommandBehavior.SOCKET))
			{
				return await services.Commands.SocketAsync(visitor, parser, src, context, command, socketCandidate.LibraryInformation);
			}

			// PennMUSH-style unambiguous prefix abbreviation for pre-login SOCKET commands
			// (e.g. "con"/"co"/"conn" -> CONNECT). Only kicks in when there was no exact match
			// above, and only while the connection has not logged in yet. If the typed token is
			// a prefix of more than one system SOCKET command name, it's ambiguous and we fall
			// through to the same "no such command" handling as an unknown command.
			if (parser.CurrentState.Executor is null && parser.CurrentState.Handle is not null)
			{
				var socketPrefixMatches = parser.CommandLibrary.Where(x
					=> x.Value.IsSystem
						 && x.Value.LibraryInformation.Attribute.Behavior.HasFlag(CommandBehavior.SOCKET)
						 && x.Key.StartsWith(command, StringComparison.CurrentCultureIgnoreCase)).ToList();

				if (socketPrefixMatches.Count == 1)
				{
					return await services.Commands.SocketAsync(visitor, parser, src, context, command,
						socketPrefixMatches[0].Value.LibraryInformation);
				}
			}

			// PennMUSH src/bsd.c do_command(): at the connect screen WHO, DOING and SESSION are the same
			// command — all three fall into dump_users(). They diverge only once a player is connected,
			// where DOING and SESSION are ordinary in-game commands with their own output. WHO already
			// carries CommandBehavior.SOCKET and answers anonymously, so the login-screen forms of the
			// other two are routed to it rather than duplicated. DOING and SESSION deliberately keep
			// CB.Default: giving them the SOCKET flag would drop them out of the in-game abbreviation
			// trie, so "doin" would stop working for a logged-in player.
			// PennMUSH matches these with strncmp, not equality, so "DOINGfoo" is DOING with a listing
			// filter of "foo" rather than an unknown command.
			if (parser.CurrentState.Executor is null && parser.CurrentState.Handle is not null
					&& (command.StartsWith("DOING", StringComparison.OrdinalIgnoreCase)
							|| command.StartsWith("SESSION", StringComparison.OrdinalIgnoreCase))
					&& parser.CommandLibrary.TryGetValue("WHO", out var who)
					&& who.IsSystem)
			{
				return await services.Commands.SocketAsync(visitor, parser, src, context, command, who.LibraryInformation);
			}

			if (parser.CurrentState.Executor is null && parser.CurrentState.Handle is not null)
			{
				await services.NotifyService.NotifyLocalized(parser.CurrentState.Handle.Value,
					nameof(ErrorMessages.Notifications.NoSuchCommandAtLogin));
				return new None();
			}

			// PennMUSH src/command.c command_parse(): before any command-table lookup, a leading
			// SAY_TOKEN ("), POSE_TOKEN (:), SEMI_POSE_TOKEN (;) or EMIT_TOKEN (\) is replaced by the
			// corresponding command name and the token character is skipped. Two details of that
			// branch matter and are reproduced here:
			//   * ';' followed by a space means POSE, not SEMIPOSE (`; waves` -> `One waves`).
			//   * `parse_switches = 0` for every replacer, so `"/noeval x` says "/noeval x" rather
			//     than invoking SAY with a NOEVAL switch.
			// Re-dispatching the rewritten line (rather than calling the command directly) keeps the
			// token forms on exactly the same path as the spelled-out commands, including @hook.
			// command_parse runs `while (*p == ' ') p++` BEFORE that switch, so the token still counts
			// when the player typed spaces in front of it: `  "hello` is a SAY. commandText is the raw
			// slice and still carries those spaces (`command` above was TrimStart()ed, commandText was
			// not), so the token test and the re-dispatched remainder both work off tokenText — one
			// value, so the slice can never be taken from a different offset than the test.
			var tokenStart = CommandArgumentSplitter.SkipSpaces(commandText, 0);
			var tokenText = tokenStart > 0
				? commandText.Substring(tokenStart, commandText.Length - tokenStart)
				: commandText;
			var speechReplacer = SpeechTokenCommand(tokenText);
			if (speechReplacer is not null)
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

			if (command[..1] == visitor.Configuration.CurrentValue.Chat.ChatTokenAlias.ToString())
			{
				var channels = services.Mediator.CreateStream(new GetChannelListQuery());
				var check = command[1..];

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

				SharpChannel? channel = null;
				if (exactMatches.Count == 1)
				{
					channel = exactMatches[0];
				}
				else if (exactMatches.Count == 0 && partialMatches.Count == 1)
				{
					channel = partialMatches[0];
				}
				else if (exactMatches.Count > 1)
				{
					if (parser.CurrentState.Handle is not null)
					{
						await services.NotifyService.NotifyLocalized(parser.CurrentState.Handle.Value,
							nameof(ErrorMessages.Notifications.AmbiguousChannelNameFormat), check);
					}

					return new None();
				}
				else if (partialMatches.Count > 1)
				{
					if (parser.CurrentState.Handle is not null)
					{
						await services.NotifyService.NotifyLocalized(parser.CurrentState.Handle.Value,
							nameof(ErrorMessages.Notifications.AmbiguousChannelNameMatchesFormat), check,
							string.Join(", ", partialMatches.Select(c => c.Name.ToPlainText())));
					}

					return new None();
				}

				if (channel is not null && !context.evaluationString().IsEmpty)
				{
					return await services.Commands.ChannelAsync(parser, channel, context, src);
				}
			}

			if (parser.CommandLibrary.TryGetAlternateValue(command.AsSpan(0, 1), out var singleTokenCandidate)
					&& singleTokenCandidate.IsSystem
					&& singleTokenCandidate.LibraryInformation.Attribute.Behavior.HasFlag(CommandBehavior.SingleToken))
			{
				return await services.Commands.SingleTokenAsync(visitor, parser, src, context, command, tokenText,
					singleTokenCandidate.LibraryInformation);
			}

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

			if (matchResult != null)
			{
				return await services.Commands.InternalAsync(visitor, parser, src, context, rootCommand, switches,
					matchResult.Value.Definition);
			}

			// Step 6: Check @attribute setting
			// Standard attributes (e.g., DESCRIBE) can be set using @attrname object=value syntax
			// This supports prefix matching when the attribute has the "prefixmatch" flag
			if (rootCommand.StartsWith('@') && context.evaluationString() != null)
			{
				var attrCommandResult = await services.StandardAttributes.TryRunAsync(parser, src, context, rootCommand);
				if (attrCommandResult.IsSome())
				{
					return attrCommandResult;
				}
			}

			// Step 7: Enter Aliases
			// Step 8: Leave Aliases

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
			Option<CallState> PreserveCommandEvaluationErrors(Option<CallState> result)
			{
				if (evaluatedCommandResult?.HadErrors != true) return result;
				return (result is CallState value ? value : CallState.Empty) with { HadErrors = true };
			}

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

			// Steps 9 and 11-15: $-commands nearby, then on the location's zone master room, the location
			// itself, the executor's personal zone master room, and the master room and its contents.
			// The first scope with a match runs it; a scope is only looked up once those before it failed.
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
					evaluatedCommandText);

				if (userDefinedCommandMatches.TryGetValue(out var matches))
				{
					return PreserveCommandEvaluationErrors(await services.Commands.UserDefinedAsync(parser, matches, inPlace));
				}
			}

			// Step 16: HUH_COMMAND is run
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
					return PreserveCommandEvaluationErrors(huhResult);
				}
			}

			// The name is synthetic; %c and %u stay the line that matched nothing.
			var newParser = parser.Push(parser.CurrentState with
			{
				Command = "HUH_COMMAND",
				Arguments = [],
				Function = null
			});

			var huhCommand = await parser.CommandLibrary["HUH_COMMAND"].LibraryInformation.Command.Invoke(newParser);

			return PreserveCommandEvaluationErrors(huhCommand);
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
