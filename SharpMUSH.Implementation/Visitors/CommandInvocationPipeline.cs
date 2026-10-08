using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Implementation.Handlers.Database;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Visitors;

/// <summary>
/// Runs a command once <see cref="CommandDispatcher"/> has decided what it is: a built-in (with its
/// hooks, plugin interceptors, switch validation, restrictions, CommandLock, VERBOSE trace and
/// telemetry), a <c>SOCKET</c> or single-token command, a <c>$</c>-command match, a hook, a chat
/// alias or an exit.
/// </summary>
/// <remarks>Stateless: one instance serves every evaluation of a parser.</remarks>
internal sealed class CommandInvocationPipeline(EvaluationServices services)
{
	/// <summary>
	/// A built-in command: splits its arguments, then runs it through its hooks, plugin interceptors,
	/// switch and lock checks.
	/// </summary>
	public async ValueTask<Option<CallState>> InternalAsync(SharpMUSHParserVisitor visitor, IMUSHCodeParser prs,
		MString src, ICommandContext context, string rootCommand, string[] switches,
		CommandDefinition libraryCommandDefinition)
	{
		var noEvalSwitch = Array.Exists(switches, s => s.Equals("NOEVAL", StringComparison.OrdinalIgnoreCase));
		var singleArgument = switches.Any(s => libraryCommandDefinition.Attribute.SingleArgumentSwitches.Contains(s, StringComparer.OrdinalIgnoreCase));
		return await services.Arguments.SplitAsync(visitor, prs, src, context, libraryCommandDefinition, rootCommand, noEvalSwitch, singleArgument) switch
		{
			CommandArguments argumentResults => await DispatchInternalCommand(visitor, prs, src, rootCommand, switches,
				libraryCommandDefinition, singleArgument, argumentResults),
			Error<string> splitError => await services.Arguments.RefuseAsync(prs, splitError.Value),
		};
	}

	/// <summary>
	/// Runs a built-in command whose arguments have split: its hooks, plugin interceptors, switch and lock
	/// checks, then the command itself.
	/// </summary>
	private async ValueTask<Option<CallState>> DispatchInternalCommand(SharpMUSHParserVisitor visitor, IMUSHCodeParser prs, MString src,
		string rootCommand, string[] switches, CommandDefinition libraryCommandDefinition, bool singleArgument,
		CommandArguments argumentResults)
	{
		var arguments = argumentResults.Values;

		var executor = await prs.CurrentState.ExecutorObject(services.Mediator);

		var namedRegisters = new Dictionary<string, MString>
		{
			["ARGS"] = src // The entire argument string before evaluation
		};

		// Commands test their switches by name, so the state carries them upper-cased once rather
		// than as a projection re-run on every lookup.
		var upperSwitches = Array.ConvertAll(switches, static s => s.ToUpperInvariant());
		if (switches.Length > 0)
		{
			namedRegisters["SWITCHES"] = MarkupText.Plain(string.Join(" ", switches));
		}

		// For EQSPLIT commands, populate LS/RS registers
		if (!singleArgument && libraryCommandDefinition.Attribute.Behavior.HasFlag(CommandBehavior.EqSplit))
		{
			var sourceText = src.ToString();
			var equalsIndex = sourceText.IndexOf('=');
			if (equalsIndex >= 0)
			{
				namedRegisters["LS"] = MarkupText.Plain(sourceText[..equalsIndex].Trim());
				namedRegisters["EQUALS"] = MarkupText.Plain("=");
				namedRegisters["RS"] = MarkupText.Plain(sourceText[(equalsIndex + 1)..].Trim());
			}
			else
			{
				namedRegisters["LS"] = src;
			}
		}
		else
		{
			namedRegisters["LS"] = src;
		}

		for (int i = 0; i < arguments.Count; i++)
		{
			namedRegisters[$"LSA{i + 1}"] = arguments[i].Message ?? MarkupText.Empty;
		}

		namedRegisters["LSAC"] = MarkupText.Plain(arguments.Count.ToString());

		var commandWithSwitches = src;

		// PennMUSH command_parse rebuilds cmd_evaled from command_argparse's results and stores it
		// before any hook runs, so /before, /after, the body and later commands all read it as %u.
		prs.CurrentState.CommandText?.EvaluatedFrom(HookInput);

		MString HookInput()
		{
			// Reuse the split arguments: evaluating the whole line loses bare function calls,
			// ignores NoParse/RSNoParse/noeval, and runs side effects a second time.
			var name = libraryCommandDefinition.Attribute.Name;
			var prefix = switches.Length == 0 ? name : $"{name}/{string.Join('/', switches)}";
			if (arguments.Count == 0) return MarkupText.Plain(prefix);

			var values = arguments.Select(argument => argument.Message ?? MarkupText.Empty);
			var eqSplit = !singleArgument && libraryCommandDefinition.Attribute.Behavior.HasFlag(CommandBehavior.EqSplit);
			var text = eqSplit && arguments.Count > 1
				? MarkupText.Concat([arguments[0].Message ?? MarkupText.Empty, MarkupText.Plain("="),
					MarkupText.Join(MarkupText.Plain(","), values.Skip(1))])
				: MarkupText.Join(MarkupText.Plain(","), values);
			return MarkupText.Concat(MarkupText.Plain(prefix + " "), text);
		}

		var dispatchResult = await prs.With(state =>
			{
				// Save caller's numbered arguments (%0-%9) before overwriting with command's own args.
				// This allows @wait/@force to preserve pattern-match variables in queued callbacks.
				var callerArgs = state.Arguments
					.Where(x => int.TryParse(x.Key, out _))
					.ToDictionary(x => x.Key, x => x.Value);

				var newState = state with
				{
					Command = rootCommand,
					Switches = upperSwitches,
					Arguments = SharpMUSHParserVisitor.NumberedArguments(arguments),
					CommandInvoker = libraryCommandDefinition.Command,
					Function = null,
					CallerArguments = callerArgs.Count > 0 ? callerArgs : null
				};

				foreach (var (key, value) in namedRegisters)
				{
					newState.AddRegister(key, value);
				}

				return newState;
			},
			async newParser =>
			{
				// A hook lives on the command, not on the name that reached it. Penn stores it on the
				// COMMAND_INFO every alias points at (command.h:161) and do_hook gets there with
				// command_find (command.c:2589), so `@hook/override say` fires for `"` as well. Keyed by
				// what was typed, a hook fired for one spelling of its command and no other (#1223).
				var hookedCommand = libraryCommandDefinition.Attribute.Name;
				var hookHadErrors = false;
				async ValueTask<Option<CallState>> EvaluateHook(CommandHook hook, Option<MString> input = null!)
				{
					var result = await HookAsync(newParser, executor, hook, input);
					hookHadErrors |= result is CallState { HadErrors: true };
					return result;
				}
				Option<CallState> PreserveHookErrors(Option<CallState> result)
				{
					if (!hookHadErrors) return result;
					return (result is CallState value ? value : CallState.Empty) with { HadErrors = true };
				}

				// 1. Check for /ignore hook
				var ignoreHook = await services.HookService.GetHookAsync(hookedCommand, "IGNORE");
				if (ignoreHook is CommandHook ignoreCode)
				{
					var ignoreResult = await EvaluateHook(ignoreCode);
					if (ignoreResult is CallState ignoreValue && ignoreValue.Message.Falsy(newParser))
					{
						return PreserveHookErrors(CallState.Empty);
					}
				}

				// 2. Check for /before hook
				var beforeHook = await services.HookService.GetHookAsync(hookedCommand, "BEFORE");
				if (beforeHook is CommandHook beforeCode)
				{
					await EvaluateHook(beforeCode);
					// Hook text is ignored, but failure state is retained.
				}

				// Phase 2b: C# command interceptors run alongside the softcode @hook flow. The dispatcher
				// no-ops (and HasCommandInterceptors is false) when no plugin registered an interceptor, so
				// normal dispatch is unchanged. The raw command-with-switches text is the interceptor's input.
				var pluginHooks = services.PluginHooks;
				var pluginCommandText = commandWithSwitches.ToPlainText();
				// before → after the softcode BEFORE: a C# interceptor returning false vetoes the command
				// (mirrors a softcode IGNORE that returns false: skip the body and run the after seam).
				if (pluginHooks is { HasCommandInterceptors: true }
					&& !await pluginHooks.CommandBeforeAsync(newParser, pluginCommandText))
				{
					await pluginHooks.CommandAfterAsync(newParser, pluginCommandText);
					return PreserveHookErrors(CallState.Empty);
				}

				// 3. Check for /override hook with $-command matching
				var overrideHook = await services.HookService.GetHookAsync(hookedCommand, "OVERRIDE");
				if (overrideHook is CommandHook overrideCode)
				{
					var overrideResult = await EvaluateHook(overrideCode, HookInput());
					if (overrideResult is CallState overridden)
					{
						// 5. Check for /after hook before returning
						var afterHook = await services.HookService.GetHookAsync(hookedCommand, "AFTER");
						if (afterHook is CommandHook afterCode)
						{
							await EvaluateHook(afterCode);
							// Hook text is ignored, but failure state is retained.
						}

						return PreserveHookErrors(overridden);
					}
				}

				// override → after the softcode OVERRIDE: a non-null C# interceptor override short-circuits
				// the built-in (mirrors a softcode OVERRIDE), still running both after seams before returning.
				if (pluginHooks is { HasCommandInterceptors: true })
				{
					var pluginOverride = await pluginHooks.CommandTryOverrideAsync(newParser, pluginCommandText);
					if (pluginOverride is not null)
					{
						var afterHook = await services.HookService.GetHookAsync(hookedCommand, "AFTER");
						if (afterHook is CommandHook afterCode)
						{
							await EvaluateHook(afterCode);
						}

						await pluginHooks.CommandAfterAsync(newParser, pluginCommandText);
						return PreserveHookErrors(pluginOverride);
					}
				}

				// Validate switches and check for /extend hook if invalid switches are found
				var allowedSwitches = libraryCommandDefinition.Attribute.Switches ?? [];
				var invalidSwitches = switches.Length == 0 || allowedSwitches.Contains("*", StringComparer.OrdinalIgnoreCase)
					? []
					: switches.Where(s => !allowedSwitches.Contains(s, StringComparer.OrdinalIgnoreCase)).ToArray();

				if (invalidSwitches.Length > 0)
				{
					// Check for /extend hook to handle invalid switches
					var extendHook = await services.HookService.GetHookAsync(hookedCommand, "EXTEND");
					if (extendHook is CommandHook extendCode)
					{
						var extendResult = await EvaluateHook(extendCode, HookInput());
						if (extendResult is CallState extended)
						{
							// Execute /after hook before returning
							var afterHook = await services.HookService.GetHookAsync(hookedCommand, "AFTER");
							if (afterHook is CommandHook afterCode)
							{
								await EvaluateHook(afterCode);
							}

							return PreserveHookErrors(extended);
						}
					}

					// No extend hook or it didn't match - return error for invalid switches.
					// PennMUSH (src/command.c) *notifies* here — `notify(executor, switch_err)` with
					// "%s doesn't know switch %s." — instead of running the command. Returning the error
					// only as a CallState made a mistyped switch silently do nothing at a prompt, which is
					// how `@shutdown/what` and `@wiki/get home` both came to complete with no output at all.
					// PennMUSH names only the first unknown switch, and names it upper-cased — the switch
					// text it formats has already been through the command line's canonicalisation
					// (oracle: `@shutdown/what` → "@SHUTDOWN doesn't know switch WHAT."). The return value
					// still lists every offender as typed, because that string is a machine-readable
					// result callers already match on.
					if (executor is AnySharpObject notifiedExecutor)
					{
						await services.NotifyService.NotifyLocalized(notifiedExecutor,
							nameof(ErrorMessages.Notifications.CommandUnknownSwitchFormat),
							libraryCommandDefinition.Attribute.Name, invalidSwitches[0].ToUpperInvariant());
					}

					var invalidSwitchList = string.Join(", ", invalidSwitches);
					return PreserveHookErrors(new CallState($"#-1 INVALID SWITCH: {invalidSwitchList}"));
				}

				// 4. Check the behaviour restrictions and CommandLock before executing
				var commandLockStr = libraryCommandDefinition.Attribute.CommandLock;
				if (executor is AnySharpObject lockedExecutor
					&& (!await SharpMUSH.Library.Services.CommandRestrictions.PermitsAsync(libraryCommandDefinition.Attribute, lockedExecutor)
						|| (!string.IsNullOrEmpty(commandLockStr) && !await services.LockService.Evaluate(commandLockStr, lockedExecutor, lockedExecutor))))
				{
					// command_check_with sends the command's restrict_message in place of "Permission
					// denied." when it has one (command.c:2337-2341).
					var restrictMessage = libraryCommandDefinition.Attribute.RestrictMessage;
					if (string.IsNullOrEmpty(restrictMessage))
					{
						await services.NotifyService.NotifyLocalized(lockedExecutor, nameof(ErrorMessages.Notifications.PermissionDenied));
					}
					else
					{
						await services.NotifyService.Notify(lockedExecutor, restrictMessage, lockedExecutor);
					}

					return PreserveHookErrors(new CallState(ErrorMessages.Returns.PermissionDenied));
				}

				// 5. Execute the built-in command
				var startTime = System.Diagnostics.Stopwatch.GetTimestamp();
				var commandSuccess = true;
				Option<CallState> commandResult;

				if (executor is AnySharpObject verboseExecutor && await verboseExecutor.HasFlag("VERBOSE"))
				{
					var verboseOutput = $"#{verboseExecutor.Object().DBRef.Number}] {commandWithSwitches.ToPlainText()}";
					await services.Diagnostics.SendDebugOrVerboseOutput(visitor.Parser, verboseExecutor, verboseOutput);
				}

				var commandText = newParser.CurrentState.CommandText;
				var outputVersion = commandText?.OutputVersion;
				var outputBefore = commandText?.Output;
				try
				{
					// Track command history for @retry support (shared mutable reference, persists across With() copies).
					// A rerun records its output as this run does, so @retry leaves the last rerun's %>.
					newParser.CurrentState.CommandHistory?.Push((Recorded(libraryCommandDefinition),
						newParser.CurrentState.Arguments));
					commandResult = await libraryCommandDefinition.Command.Invoke(newParser);
				}
				catch (Exception)
				{
					commandSuccess = false;
					throw; // Re-throw, so commandResult will never be accessed uninitialized
				}
				finally
				{
					var elapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
					// The registered name, not the abbreviation or case typed, so @pe and @PEMIT are one series.
					services.Telemetry?.RecordCommandInvocation(libraryCommandDefinition.Attribute.Name.ToUpperInvariant(), elapsedMs, commandSuccess);
				}

				// %> is recorded before the after hook runs, so the hook reads the command's own output.
				RecordOutput(commandText, outputVersion, outputBefore, libraryCommandDefinition.Attribute.Output, commandResult);

				// 5. Check for /after hook
				var afterHookFinal = await services.HookService.GetHookAsync(hookedCommand, "AFTER");
				if (afterHookFinal is CommandHook afterFinalCode)
				{
					await EvaluateHook(afterFinalCode);
					// Hook text is ignored, but failure state is retained.
				}

				// after → near the softcode AFTER: C# interceptors observe the completed command. Result discarded.
				if (pluginHooks is { HasCommandInterceptors: true })
				{
					await pluginHooks.CommandAfterAsync(newParser, pluginCommandText);
				}

				return PreserveHookErrors(commandResult);
			});
		return argumentResults.Preserve(dispatchResult);
	}

	/// <summary>
	/// Records a built-in command's <c>%></c> as its <see cref="CommandOutput"/> declares. A command that
	/// records nothing here — <see cref="CommandOutput.None"/>, or <see cref="CommandOutput.Runs"/> when
	/// the list it ran was queued — is cleared by <see cref="CommandDispatcher"/> once it returns.
	/// </summary>
	private static void RecordOutput(CommandText? commandText, long? versionBefore, MString? outputBefore,
		CommandOutput kind, Option<CallState> result)
	{
		if (commandText is null) return;
		switch (kind)
		{
			case CommandOutput.Value:
				commandText.SetOutput(result is CallState { Message: { } message } ? message : MarkupText.Empty);
				break;
			case CommandOutput.Passthrough:
				// Restored, not kept: an action list @assert or @break ran in place may have recorded over it.
				commandText.SetOutput(outputBefore ?? commandText.Output);
				break;
			case CommandOutput.Runs when commandText.OutputVersion != versionBefore:
				// The in-place list it ran recorded its last command's output into this same entry.
				commandText.KeepOutput();
				break;
		}
	}

	/// <summary>
	/// The command as <c>@retry</c> reruns it: invoked directly, outside this pipeline, so it records its
	/// own <c>%></c> here.
	/// </summary>
	private static Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Recorded(CommandDefinition definition)
		=> async parser =>
		{
			var commandText = parser.CurrentState.CommandText;
			var version = commandText?.OutputVersion;
			var before = commandText?.Output;
			var result = await definition.Command.Invoke(parser);
			RecordOutput(commandText, version, before, definition.Attribute.Output, result);
			return result;
		};

	/// <summary>
	/// Executes hook code from an attribute on an object.
	/// For OVERRIDE and EXTEND hooks, performs $-command matching.
	/// For other hooks, executes the attribute directly.
	/// Handles inline execution with proper q-register management.
	/// </summary>
	public async ValueTask<Option<CallState>> HookAsync(IMUSHCodeParser localParser,
		AnyOptionalSharpObject executor,
		CommandHook hook, Option<MString> commandInput = null!)
	{
		var targetObject = await services.Mediator.Send(new GetObjectNodeQuery(hook.TargetObject));
		if (targetObject is not AnySharpObject targetObj)
		{
			return new None();
		}

		var executorObj = executor is AnySharpObject knownExecutor ? knownExecutor : targetObj;

		// /localize and /clearregs only apply to an inline hook.
		using var registers = RegisterScope.Enter(localParser.CurrentState.Registers,
			hook is { Inline: true, Localize: true }, hook is { Inline: true, ClearRegs: true });

		// For OVERRIDE and EXTEND hooks, perform $-command matching
		if (hook.HookType is "OVERRIDE" or "EXTEND" && commandInput is MString input)
		{
			// run_cmd_hook (command.c:2459-2465): a hook that names an attribute tries only that one
			// (one_comm_match); a hook without one tries every $-command on the object (atr_comm_match).
			var matchResult = string.IsNullOrEmpty(hook.AttributeName)
				? await services.CommandDiscoveryService.MatchUserDefinedCommand(
					localParser,
					new[] { targetObj }.ToAsyncEnumerable(),
					input,
					executorObj)
				: await MatchOneCommandAsync(targetObj, executorObj, hook.AttributeName, input);

			if (!matchResult.TryGetValue(out var matches))
			{
				return new None();
			}

			// run_cmd_hook (command.c:2454) hands hook->inplace to atr_comm_match as the queue type, so
			// the matched body runs in place only for an /inline hook. Otherwise parse_que_attr queues
			// it as its own entry, after the current action list, with fresh q-registers.
			return await UserDefinedAsync(localParser, matches, inPlace: hook.Inline);
		}

		// run_hook (command.c:2406-2433) reads the hook attribute with a bare atr_get: the wizard who set
		// the @hook chose the code, so the player whose command triggered it needs no right to read it.
		return await services.AttributeService.EvaluateAttributeFunctionResultAsync(localParser, executorObj, targetObj,
			hook.AttributeName, new Dictionary<string, CallState>(), evalParent: true, ignorePermissions: true);
	}

	/// <summary>
	/// PennMUSH <c>one_comm_match</c> (<c>src/attrib.c:2132-2181</c>): the one <c>$</c>-command
	/// <paramref name="attributeName"/> names on <paramref name="thing"/>, read through its parents and
	/// type ancestor as <c>atr_get_with_parent</c> reads it, matched against <paramref name="input"/>.
	/// A HALT or NO_COMMAND object, a no_command attribute or branch, a value that is not a
	/// <c>$</c>-command, and a refusal by <paramref name="player"/>'s @lock/command or @lock/use on
	/// <paramref name="thing"/> are all no match.
	/// </summary>
	private async ValueTask<Option<IEnumerable<(AnySharpObject SObject, SharpAttribute Attribute, Dictionary<string, CallState> Arguments)>>> MatchOneCommandAsync(
		AnySharpObject thing, AnySharpObject player, string attributeName, MString input)
	{
		if (await thing.HasFlag("HALT") || await thing.HasFlag("NO_COMMAND"))
		{
			return new None();
		}

		// atr_get_with_parent with cmd set refuses AF_NOPROG on the attribute and on every branch above it.
		if (await services.AttributeService.GetAttributeAsync(thing, thing, attributeName,
					IAttributeService.AttributeMode.Read, parent: true) is not SharpAttribute[] { Length: > 0 } chain
				|| chain.Any(segment => segment.IsNoprog())
				|| CommandAttributeScanner.Compile(chain[^1]) is not CommandAttributeCache command)
		{
			return new None();
		}

		var trimmed = input.Trim(TrimType.TrimBoth);
		if (SoftcodeRegex.Match(command.CompiledRegex, trimmed.ToPlainText()) is not { Success: true } match
				|| !await services.LockService.Evaluate(LockType.Command, thing, player)
				|| !await services.LockService.Evaluate(LockType.Use, thing, player))
		{
			return new None();
		}

		return Option<IEnumerable<(AnySharpObject SObject, SharpAttribute Attribute, Dictionary<string, CallState> Arguments)>>
			.FromOption([(thing, command.Attribute, PatternArguments.Capture(command.CompiledRegex, match, command.IsRegexFlag, trimmed))]);
	}

	/// <summary>
	/// Runs the bodies of every matched <c>$</c>-command, with the matching object as executor.
	/// </summary>
	/// <remarks>
	/// PennMUSH's <c>atr_comm_match</c> (<c>src/attrib.c:2056-2093</c>) runs a match in place only when
	/// the command carries <c>QUEUE_INPLACE</c>, which <c>process_command</c> sets for a command that
	/// arrived on a socket (<c>src/game.c:1224-1225</c>). Every other match — one reached from
	/// <c>@force</c>, <c>@trigger</c> or any other action list — is queued as a new entry by
	/// <c>parse_que_attr</c>, so it runs after the list that matched it, with its own q-registers and
	/// budget, and <c>@halt</c> can drop it. <see cref="ParserStateFlags.DirectInput"/> is set only for a
	/// command typed at a connection, so it stands for the socket here. <see cref="HookAsync"/>'s
	/// OVERRIDE/EXTEND path runs in place only for an <c>/inline</c> hook (<c>run_cmd_hook</c>,
	/// <c>src/command.c:2454</c>).
	/// </remarks>
	public async ValueTask<Option<CallState>> UserDefinedAsync(
		IMUSHCodeParser prs,
		IEnumerable<(AnySharpObject Obj, SharpAttribute Attr, Dictionary<string, CallState> Arguments)> matches,
		bool inPlace = true)
	{
		CallState? failure = null;
		// A `]` or `~` in front of the typed command only governs how that command is read and matched
		// (src/command.c:1160-1166 strips NOEVAL_TOKEN, then matches the unevaluated line). The body is its
		// own queue entry and runs with default evaluation, so the modifier's parse state stops here.
		var bodyState = prs.CurrentState with
		{
			ParseMode = ParseMode.Default,
			Flags = prs.CurrentState.Flags & ~(ParserStateFlags.PreserveBraces | ParserStateFlags.StrictParse),
			CommandModifierDepth = 0
		};
		foreach (var (obj, attr, arguments) in matches)
		{
			// A HALTED object runs no softcode (PennMUSH PE_NOTHING for a Halted executor), so its
			// $-commands do not fire — the same rule enforced for u()/ufun in AttributeService. This
			// is what makes @chown's loop-break (which halts the object) actually stop the loop.
			if (await obj.HasFlag("HALT"))
			{
				continue;
			}

			if (!inPlace)
			{
				// parse_que_attr queues with the matching command's executor as both enactor and caller.
				await QueuedCommandMatch.Admit(services.Mediator, bodyState, obj, attr, arguments,
					prs.CurrentState.Executor, ExecutionBudget.CurrentToken);
				continue;
			}

			var body = attr.Value.Substring(attr.CommandListIndex!.Value, attr.Value.Length - attr.CommandListIndex!.Value);

			// In place, the body is still its own queue entry in PennMUSH (PE_INFO_DEFAULT): it starts with
			// no %c/%u, and what it runs never reaches the command that matched it. Its enactor and caller are
			// the object that ran the matching command (atr_comm_match's new_queue_actionlist_int(thing, player,
			// player, ...), src/attrib.c), so a forced player's inline-hooked `say` speaks as that player.
			var newParser = prs.Push(bodyState with
			{
				CurrentEvaluation = new DBAttribute(obj.Object().DBRef, attr.LongName),
				EnvironmentRegisters = arguments,
				Arguments = arguments,
				Function = null,
				Executor = obj.Object().DBRef,
				Enactor = prs.CurrentState.Executor,
				Caller = prs.CurrentState.Executor,
				// No %c/%u yet: CommandListParse starts them, and treats the body as a queue entry's own
				// list at in-place depth 0, so its nested lists count from 1 (src/cque.c:1182). Its %>
				// and %| start as copies of the matching list's, as a queued body's do.
				CommandText = null,
				QueuedOutput = prs.CurrentState.PipedOutput,
				QueuedPrinted = prs.CurrentState.PrintedOutput
			});

			var result = await newParser.CommandListParse(body);
			if (result?.HadErrors == true) failure ??= result;
		}

		return failure ?? CallState.Empty;
	}

	/// <summary>A <c>SOCKET</c> command: one the connection answers itself, logged in or not.</summary>
	public async ValueTask<Option<CallState>> SocketAsync(SharpMUSHParserVisitor visitor, IMUSHCodeParser prs, MString src,
		ICommandContext context, string command, CommandDefinition librarySocketCommandDefinition)
	{
		// The typed token is passed as the root command so SplitAsync's no-space branch strips it.
		// Without it a bare "IDLE" splits to a single argument equal to "IDLE" itself, and every socket
		// command that reads an optional argument sees its own name as that argument: bare OUTPUTPREFIX
		// set the prefix to "OUTPUTPREFIX" instead of clearing it, and bare SOCKSET asked for an option
		// and a value instead of reporting the settings. `command` rather than the library name because
		// realSubtext holds what the player typed, which may be an unambiguous abbreviation of it.
		return await services.Arguments.SplitAsync(visitor, prs, src, context, librarySocketCommandDefinition, command) switch
		{
			CommandArguments argumentResults =>
				await DispatchSocketCommand(prs, command, librarySocketCommandDefinition, argumentResults),
			Error<string> splitError => await services.Arguments.RefuseAsync(prs, splitError.Value),
		};
	}

	/// <summary>Runs a socket command whose arguments have split.</summary>
	private static async ValueTask<Option<CallState>> DispatchSocketCommand(IMUSHCodeParser prs, string command,
		CommandDefinition librarySocketCommandDefinition, CommandArguments argumentResults)
	{
		var arguments = argumentResults.Values;

		var dispatchResult = await prs.With(state => state with
		{
			Command = command,
			Arguments = SharpMUSHParserVisitor.NumberedArguments(arguments),
			Function = null
		}, async newParser => await librarySocketCommandDefinition.Command.Invoke(newParser));
		return argumentResults.Preserve(dispatchResult);
	}

	/// <summary>A single-token command (<c>&amp;</c>, <c>]</c>, <c>~</c>, ...): the token is the command and what is glued to it its first argument.</summary>
	public async ValueTask<Option<CallState>> SingleTokenAsync(SharpMUSHParserVisitor visitor, IMUSHCodeParser prs,
		MString src, ICommandContext context, string command, MString tokenText, CommandDefinition singleLibraryCommandDefinition)
	{
		var singleRootCommand = command[..1];

		// Modifiers carry one exact source slice. Attribute assignment retains its separate
		// glued-name/LHS/RHS argument contract below.
		if (singleRootCommand is "]" or "~")
			return await prs.With(state => state with
			{
				Arguments = new() { ["0"] = new CallState(tokenText.Substring(1)) },
				Function = null
			}, async modified => await singleLibraryCommandDefinition.Command.Invoke(modified));

		var rest = command[1..];
		return await services.Arguments.SplitAsync(visitor, prs, src, context, singleLibraryCommandDefinition, singleRootCommand) switch
		{
			CommandArguments argumentResults => await DispatchSingleTokenCommand(prs, singleRootCommand, rest,
				singleLibraryCommandDefinition, argumentResults),
			Error<string> splitError => await services.Arguments.RefuseAsync(prs, splitError.Value),
		};
	}

	/// <summary>
	/// Runs a single-token command whose arguments have split, with the text glued to the token as <c>%0</c>.
	/// </summary>
	private static async ValueTask<Option<CallState>> DispatchSingleTokenCommand(IMUSHCodeParser prs,
		string singleRootCommand, string rest, CommandDefinition singleLibraryCommandDefinition,
		CommandArguments argumentResults)
	{
		var arguments = argumentResults.Values;

		// `&` is the only token that splits arguments; command_isattr names it ATTRIB_SET/<attribute>.
		// Its value is deferred, so %u carries it as written, which is what the command stores for
		// direct input.
		prs.CurrentState.CommandText?.EvaluatedFrom(() =>
		{
			var values = arguments.Select(argument => argument.Message ?? MarkupText.Empty).ToList();
			var name = MarkupText.Plain($"ATTRIB_SET/{rest.ToUpperInvariant()} ");
			return values.Count > 1
				? MarkupText.Concat([name, values[0], MarkupText.Plain("="), MarkupText.Join(MarkupText.Plain(","), values.Skip(1))])
				: MarkupText.Concat(name, values.FirstOrDefault() ?? MarkupText.Empty);
		});

		// %0 is the text glued to the token itself; the split arguments follow from %1.
		var numbered = new Dictionary<string, CallState>(arguments.Count + 1) { ["0"] = new CallState(rest) };
		foreach (var (i, argument) in arguments.Index())
		{
			numbered[(i + 1).ToString()] = argument;
		}

		var dispatchResult = await prs.With(state =>
				state with
				{
					Command = singleRootCommand,
					Arguments = numbered,
					Function = null
				},
			async newParser => await singleLibraryCommandDefinition.Command.Invoke(newParser)
		);
		return argumentResults.Preserve(dispatchResult);
	}

	/// <summary>A chat alias (<c>+channel message</c>): runs <c>@CHAT</c> on the matched channel.</summary>
	public async Task<Option<CallState>> ChannelAsync(IMUSHCodeParser prs, SharpChannel channel,
		ICommandContext context, MString src)
	{
		var full = src.Substring(context.evaluationString().Start.StartIndex, context.evaluationString().Stop.StopIndex - context.evaluationString().Start.StartIndex + 1);
		var aliasStart = CommandArgumentSplitter.SkipSpaces(full, 0);
		if (aliasStart > 0)
		{
			full = full.Substring(aliasStart, full.Length - aliasStart);
		}

		// The evaluation string still carries any indent and the `+<channel>` token itself; only what
		// follows the first space after the token is the message. Without this, `+Public Hi` was chatted
		// as the literal "+Public Hi".
		var firstSpace = full.IndexOf(" ");
		var rest = firstSpace == -1
			? MarkupText.Empty
			: full.Substring(firstSpace + 1, full.Length - firstSpace - 1);

		// PennMUSH rewrites the alias to `@CHAT <channel>=<message>` before rebuilding it for %u.
		prs.CurrentState.CommandText?.EvaluatedFrom(() => MarkupText.Concat(
			[MarkupText.Plain("@CHAT "), channel.Name, MarkupText.Plain("="), rest]));

		var chatParser = prs.Push(prs.CurrentState with
		{
			Command = "@CHAT",
			Arguments = new Dictionary<string, CallState>
			{
				{ "0", new CallState(channel.Name) },
				{ "1", new CallState(rest) }
			}
		});

		return await chatParser.CommandLibrary["@CHAT"].LibraryInformation.Command.Invoke(chatParser);
	}

	/// <summary>A matched exit: runs <c>GOTO</c> through it.</summary>
	/// <param name="typedName">
	/// The exit name or alias the player actually typed. PennMUSH passes this to a variable exit's
	/// DESTINATION attribute as %0 (move.c:369), so one exit can route differently per alias.
	/// </param>
	public static async ValueTask<Option<CallState>> GoAsync(
		IMUSHCodeParser prs, SharpExit exit, string typedName)
	{
		// command_parse turns a matched exit into `GOTO <exit>`, which is what %u records.
		prs.CurrentState.CommandText?.Evaluated = MarkupText.Plain($"GOTO {typedName}");

		var newParser = prs.Push(prs.CurrentState with
		{
			Command = "GOTO",
			Arguments = new Dictionary<string, CallState>
			{
				{ "0", new CallState(exit.Object.DBRef.ToString(), 0) },
				{ "1", new CallState(typedName, 0) }
			},
			Function = null
		});

		var gotoCommand = newParser.CommandLibrary["GOTO"].LibraryInformation;
		var result = await gotoCommand.Command.Invoke(newParser);
		RecordOutput(prs.CurrentState.CommandText, null, null, gotoCommand.Attribute.Output, result);
		return result;
	}
}
