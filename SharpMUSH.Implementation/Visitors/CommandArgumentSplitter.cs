using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Visitors;

/// <summary>
/// Splits and evaluates a command's arguments: PennMUSH's <c>command_argparse</c>, including the
/// <c>/noeval</c> and leading-<c>]</c> rules, the EqSplit/RSArgs/NoParse/RSNoParse behaviors, and
/// re-visiting the split's retained parse subtrees rather than re-parsing each argument.
/// </summary>
internal sealed class CommandArgumentSplitter(EvaluationServices services)
{
	/// <summary>
	/// Splits a command's arguments the way its <see cref="CommandBehavior"/> asks and evaluates the
	/// ones it evaluates, or reports why they would not split.
	/// </summary>
	/// <param name="visitor">The visitor running the command, whose options and logger a re-visited
	/// argument subtree is evaluated under.</param>
	/// <param name="prs">The command's parser.</param>
	/// <param name="src">The source the command was parsed from.</param>
	/// <param name="context">The command.</param>
	/// <param name="libraryCommandDefinition">The command being run.</param>
	/// <param name="rootCommand">The command name as typed, stripped when the arguments follow it with no space.</param>
	/// <param name="noEvalSwitch">Whether the command was given <c>/NOEVAL</c>.</param>
	/// <param name="singleArgument">Whether a switch makes the command take its arguments as one.</param>
	public async ValueTask<Result<CommandArguments>> SplitAsync(SharpMUSHParserVisitor visitor, IMUSHCodeParser prs,
		MString src, ICommandContext context, CommandDefinition libraryCommandDefinition, string? rootCommand = null,
		bool noEvalSwitch = false, bool singleArgument = false)
	{
		var argCallState = CallState.EmptyArgument;
		var behavior = libraryCommandDefinition.Attribute.Behavior;
		if (singleArgument) behavior &= ~(CommandBehavior.EqSplit | CommandBehavior.RSArgs);

		// PennMUSH's command_parse computes `noeval = SW_ISSET(sw, SWITCH_NOEVAL) || noevtoken` and
		// hands it to command_argparse, so /noeval suppresses evaluation for ANY command that takes
		// the switch — `say/noeval [add(1,2)]` says "[add(1,2)]". The leading ] mode applies to
		// both EQSPLIT sides. The explicit switch on an EQSPLIT command is the other branch
		// (command.c:1436-1446): with an '=' the left side is evaluated after all and only the right
		// side is left raw, so `@force/noeval *Alice=think %!` hands Alice `%!` to evaluate herself;
		// without one the left side is the whole argument and stays raw.
		var noEval = prs.CurrentState.ParseMode == ParseMode.NoEval
			|| noEvalSwitch && !behavior.HasFlag(CommandBehavior.EqSplit);
		var noEvalEqSplitSwitch = noEvalSwitch && behavior.HasFlag(CommandBehavior.EqSplit);

		// Do not parse the argument splitting.
		// Set PreserveBraces so VisitBracePattern preserves outer braces when:
		// - RSBrace (PennMUSH CS_BRACES): commands like @wait, @force, @halt preserve braces
		//   during parsing, then strip them at execution time via StripOuterBraces.
		//   Also used for & (attribute value storage): player-typed `& ATTR OBJ={code}` must
		//   store the braces verbatim so get(OBJ/ATTR) returns `{code}`, matching PennMUSH.
		// - NoParse (PennMUSH QUEUE_NOLIST/noeval): commands like ] store the raw value text.
		//   In PennMUSH, noeval arguments never go through process_expression, so braces
		//   naturally survive. In SharpMUSH, the ANTLR walk still processes them, so we
		//   preserve braces via the flag to match PennMUSH behavior.
		var preserveBraces = behavior.HasFlag(CommandBehavior.RSBrace)
												 || behavior.HasFlag(CommandBehavior.NoParse)
												 || noEval
												 || noEvalEqSplitSwitch;
		var newFlags = preserveBraces
			? prs.CurrentState.Flags | ParserStateFlags.PreserveBraces
			: prs.CurrentState.Flags & ~ParserStateFlags.PreserveBraces;
		var newNoParseParser = prs.Push(prs.CurrentState with { ParseMode = ParseMode.NoParse, Flags = newFlags });
		var realSubtext = src.Substring(context.evaluationString().Start.StartIndex, context.evaluationString().Stop.StopIndex - context.evaluationString().Start.StartIndex + 1);

		// PennMUSH's command_parse skips leading spaces (`while (*p == ' ') p++`) before it reads the
		// command name, so `  say hi` says "hi". CommandDispatcher.DispatchAsync already TrimStart()s to find the
		// command name; without the same trim here the first space would read as the name/argument
		// boundary and the command name itself would land in the argument.
		var leadingSpaces = SkipSpaces(realSubtext, 0);
		if (leadingSpaces > 0)
		{
			realSubtext = realSubtext.Substring(leadingSpaces, realSubtext.Length - leadingSpaces);
		}

		var spaceInContext = realSubtext.IndexOf(" ");

		// The exact text the NoParse pass below parses to produce argCallState. Retained
		// IEvaluationStringContext nodes on argCallState.ArgumentContexts have token offsets
		// relative to THIS text (not the command's full source line), so re-visiting them later
		// in EvaluateArgumentSubtree requires a visitor whose `source` field is this same MString.
		var parsedArgumentText = MarkupText.Empty;

		// command (space) argument(s)
		if (spaceInContext != -1)
		{
			// PennMUSH's command_argparse (src/command.c) opens each argument with
			// `while (*f == ' ') f++`, so EVERY space between the command name and its argument is
			// eaten, not just the one that ended the command word: `say   hi` says "hi", and so do
			// `pose   waves` and `"  hi`. Skipping only one space left the rest inside the argument.
			var argumentStart = SkipSpaces(realSubtext, spaceInContext);
			var remainder = realSubtext.Substring(argumentStart, realSubtext.Length - argumentStart);
			parsedArgumentText = remainder;

			// Nothing but trailing spaces after the command name: the command has no arguments at all
			// (`say ` is `say`), so leave the EmptyArgument sentinel in place rather than splitting "".
			if (remainder.Length == 0)
			{
				return new CommandArguments();
			}

			argCallState = await SplitAs(newNoParseParser, behavior, remainder);
		}
		else if (realSubtext.Length > 0)
		{
			// No space found but the realSubtext is non-empty.
			// This can happen when the command name is directly followed by its arguments without a space
			// (e.g., "addcom=Public" where "addcom" is the command and "=Public" is the arg,
			//  or "@retry gt(%0,-1)=dec(%0)" where the args portion has no space).
			// Strip the command name prefix (if present) to get just the arguments portion.
			var realSubtextStr = realSubtext.ToPlainText();
			var argsStr = realSubtextStr;
			if (!string.IsNullOrEmpty(rootCommand)
					&& realSubtextStr.StartsWith(rootCommand, StringComparison.OrdinalIgnoreCase))
			{
				argsStr = realSubtextStr[rootCommand.Length..];
			}

			// Strip any switch prefixes (e.g., "/type" in "@respond/type") that appear before
			// the actual argument. Switches start with '/' and precede the first space or end of string.
			while (argsStr.StartsWith('/'))
			{
				var nextSlash = argsStr.IndexOf('/', 1);
				var nextSpace = argsStr.IndexOf(' ', 1);
				int endPos;
				if (nextSlash >= 0 && (nextSpace < 0 || nextSlash < nextSpace))
					endPos = nextSlash;
				else if (nextSpace >= 0)
					endPos = nextSpace;
				else
					endPos = argsStr.Length;
				argsStr = argsStr[endPos..].TrimStart();
			}

			if (argsStr.Length > 0)
			{
				var argsSubtext = MarkupText.Plain(argsStr);
				parsedArgumentText = argsSubtext;
				argCallState = await SplitAs(newNoParseParser, behavior, argsSubtext);
			}
		}

		var argumentResults = new CommandArguments();
		var arguments = argumentResults.Values;

		var eqSplit = behavior.HasFlag(CommandBehavior.EqSplit);
		var noParse = behavior.HasFlag(CommandBehavior.NoParse) || noEval;
		var noRsParse = behavior.HasFlag(CommandBehavior.RSNoParse);
		var nArgs = argCallState?.Arguments?.Length;

		// TODO: Implement lsargs (list-style arguments) support.
		// No immediate commands require this feature yet, so implementation is deferred.
		// Also return early when Arguments is empty (EmptyArgument sentinel), meaning no args were provided.
		if (argCallState is null or { Arguments: [] })
		{
			return argumentResults;
		}

		// Parse failure: the argument split detected a syntax error. Bubble it up as Error<string>.
		if (argCallState is { Arguments: null })
		{
			var errorText = (argCallState.Message ?? MarkupText.Empty).ToPlainText();
			return new Error<string>(errorText);
		}

		// Retained parse-tree nodes from the NoParse pass above, index-parallel to
		// argCallState.Arguments — see CallState.ArgumentContexts. Reused below so evaluated
		// arguments can be produced by re-visiting the already-lexed/parsed subtree (via
		// EvaluateArgumentSubtree) instead of running FunctionParse's full lex+parse pipeline
		// a third time on text the NoParse pass already tokenized and structured.
		var argContexts = argCallState.ArgumentContexts ?? [];
		object? ContextAt(int i) => i >= 0 && i < argContexts.Length ? argContexts[i] : null;

		// The split pass above always runs lenient (CommandCommaArgsParse etc. pass
		// lenient: !StrictParse, and StrictParse is only ever set by the single-token command
		// handler) — so a syntax error in the argument text does NOT surface as the
		// Arguments:null branch above; ANTLR's LenientErrorStrategy recovers and the split still
		// returns a best-effort Arguments/ArgumentContexts array with CallState.HadErrors set.
		// Before this optimization, that didn't matter: every argument's raw text got an
		// independent STRICT re-parse via FunctionParse afterwards, which would surface the
		// error as #-1 PARSER FAILURE. EvaluateArgumentSubtree must not trust a retained subtree
		// built from an error-recovered parse, so when the split had errors ANYWHERE, every
		// argument from it falls back to that original strict re-parse — matching pre-optimization
		// behavior exactly rather than risking a wrong best-effort value for a malformed argument.
		var splitHadErrors = argCallState.HadErrors;

		if (eqSplit)
		{
			// The LHS of an EqSplit command is evaluated unless the command declares full NoParse.
			// Commands that only want their RHS unevaluated (like &) use RSNoParse instead of NoParse,
			// so that the LHS (object reference) is evaluated normally here while the RHS is deferred.
			// For a full-NoParse EqSplit command the LHS stays raw, BUT we still attach a
			// deferred ParsedMessage (mirroring the RHS args below) so a command that opts
			// to evaluate its LHS — e.g. @SCENE, which evaluates args itself unless /NOEVAL —
			// can do so. Without this, the raw LHS (e.g. "[scenewhere(%L)]") never evaluated.
			var noParseLhs = argCallState.Arguments.FirstOrDefault() ?? MarkupText.Empty;
			arguments.Add(noParse || (noEvalEqSplitSwitch && nArgs < 2)
				? DeferredArgument(noParseLhs)
				: (await EvaluateArgumentSubtree(visitor, prs, parsedArgumentText, ContextAt(0), noParseLhs, emitSubstDebug: false, splitHadErrors))!);

			if (nArgs < 2) return argumentResults;

			if (noRsParse || noParse || noEvalEqSplitSwitch)
			{
				arguments.AddRange(argCallState.Arguments!
					.Skip(1)
					.Select(DeferredArgument));
			}
			else
			{
				await EvaluateArgumentsInto(arguments, argCallState.Arguments, firstIndex: 1);
			}
		}
		else
		{
			if (noParse)
			{
				// Attach a deferred ParsedMessage so a self-evaluating NoParse command
				// (e.g. @SCENE/undo <poseId>) can evaluate a functional single arg on demand.
				arguments.AddRange(argCallState.Arguments
					.Select(DeferredArgument));
			}
			else
			{
				await EvaluateArgumentsInto(arguments, argCallState.Arguments, firstIndex: 0);
			}
		}

		return argumentResults;

		CallState DeferredArgument(MString text)
		{
			async ValueTask<CallState?> Evaluate() => argumentResults.Record(await prs.FunctionParse(text));
			return new CallState(text, argCallState.Depth, null, async () => (await Evaluate())?.Message)
			{ ParsedResult = Evaluate };
		}

		// Arguments evaluate left to right, one at a time, each against the parse-tree slot it came from.
		async ValueTask EvaluateArgumentsInto(List<CallState> target, MString[] raw, int firstIndex)
		{
			for (var i = firstIndex; i < raw.Length; i++)
			{
				target.Add((await EvaluateArgumentSubtree(visitor, prs, parsedArgumentText, ContextAt(i), raw[i],
					emitSubstDebug: true, splitHadErrors))!);
			}
		}
	}

	/// <summary>
	/// The one split a command's arguments get, chosen by its behavior: <c>arg0 = arg1,still arg 1</c>
	/// (EqSplit and RSArgs), <c>arg0 = arg1,arg2</c> (EqSplit), <c>arg0,arg1,arg2</c> (RSArgs), or the
	/// whole text as one argument.
	/// </summary>
	private static ValueTask<CallState?> SplitAs(IMUSHCodeParser parser, CommandBehavior behavior, MString text)
		=> behavior.HasFlag(CommandBehavior.EqSplit) && behavior.HasFlag(CommandBehavior.RSArgs)
			? parser.CommandEqSplitArgsParse(text)
			: behavior.HasFlag(CommandBehavior.EqSplit)
				? parser.CommandEqSplitParse(text)
				: behavior.HasFlag(CommandBehavior.RSArgs)
					? parser.CommandCommaArgsParse(text)
					: parser.CommandSingleArgParse(text);

	/// <summary>
	/// Evaluates a single command argument by re-visiting the parse subtree retained from the
	/// NoParse argument-splitting pass (<see cref="CallState.ArgumentContexts"/>) instead of
	/// re-lexing and re-parsing the argument's raw text a third time via
	/// <see cref="IMUSHCodeParser.FunctionParse(MString, bool)"/>. Falls back to the original
	/// FunctionParse pipeline when no retained context is available for this slot (e.g. an empty
	/// comma-separated argument).
	/// </summary>
	/// <param name="visitor">The visitor running the command, whose logger and options the subtree is evaluated under.</param>
	/// <param name="prs">The command's own parser — equal to the visitor's own parser at every current
	/// call site (all three SplitAsync callers pass it through unchanged).</param>
	/// <param name="argumentSourceText">
	/// The exact text the NoParse pass parsed to produce <paramref name="retainedContext"/>
	/// (SplitAsync's local <c>remainder</c>/<c>argsSubtext</c>) — the retained context's token
	/// offsets are relative to this text, NOT the command's full source line, so a sub-visitor
	/// evaluating it must be constructed with this exact text as its own `source`.
	/// </param>
	/// <param name="retainedContext">The <see cref="CallState.ArgumentContexts"/> slot for this
	/// argument (an <c>IEvaluationStringContext</c> boxed as <see cref="object"/>), or null.</param>
	/// <param name="argument">The raw argument text — same value FunctionParse's `text` parameter
	/// would receive on the fallback path.</param>
	/// <param name="emitSubstDebug">Mirrors <see cref="IMUSHCodeParser.FunctionParse(MString, bool)"/>'s
	/// substitution-only QUEUE_DEBUG trace flag.</param>
	/// <param name="splitHadErrors">
	/// <see cref="CallState.HadErrors"/> from the NoParse split pass that produced
	/// <paramref name="retainedContext"/>. That pass always runs lenient, so a syntax error
	/// anywhere in the split doesn't fail it outright — ANTLR's error recovery just produces a
	/// best-effort tree. When true, this argument (and every sibling from the same split) falls
	/// back to the strict re-parse, matching pre-optimization behavior instead of trusting a
	/// recovered tree.
	/// </param>
	private async ValueTask<CallState?> EvaluateArgumentSubtree(
		SharpMUSHParserVisitor visitor,
		IMUSHCodeParser prs,
		MString argumentSourceText,
		object? retainedContext,
		MString argument,
		bool emitSubstDebug,
		bool splitHadErrors)
	{
		// A syntax error anywhere in the split (splitHadErrors) falls back to the strict re-parse —
		// see the parameter doc above and CallState.HadErrors.
		if (retainedContext is not IEvaluationStringContext ctx
				|| splitHadErrors)
		{
			return await prs.FunctionParse(argument, emitSubstDebug);
		}

		var evalParser = prs.ForTrackedEvaluation();

		// A fresh visitor per call mirrors ParseInternalCore's "new SharpMUSHParserVisitor(...)
		// per parse": its diagnostic-suppression flag must start false for THIS argument alone, not
		// be shared/polluted by the outer command's own visitor (`this`), which is handling the
		// whole command line and whose flag may already be set from a sibling argument or an
		// enclosing function call.
		var subVisitor = new SharpMUSHParserVisitor(visitor.Logger, evalParser, visitor.Configuration, services,
			argumentSourceText);

		var result = await subVisitor.Visit(ctx);

		if (emitSubstDebug)
		{
			var rawText = argument.ToPlainText();
			await EvaluationDiagnostics.EmitSubstitutionOnlyDebugTraceAsync(
				services.Mediator, services.NotifyService, prs.CurrentState, rawText, result?.Message, subVisitor.SuppressSubstitutionOnlyDebugTrace);
		}

		return result;
	}

	/// <summary>
	/// Tells the handle that typed the command why its arguments would not split, and runs nothing. Shared by
	/// every command pattern that splits arguments.
	/// </summary>
	public async ValueTask<Option<CallState>> RefuseAsync(IMUSHCodeParser prs, string reason)
	{
		if (prs.CurrentState.Handle.HasValue)
			await services.NotifyService.Notify(prs.CurrentState.Handle.Value, reason);
		return new CallState(reason) { HadErrors = true };
	}

	/// <summary>
	/// The index of the first non-space character at or after <paramref name="from"/>, or the length of
	/// <paramref name="text"/> when there is none.
	/// </summary>
	public static int SkipSpaces(MString text, int from)
	{
		var plain = text.ToPlainText();
		var index = from;
		while (index < plain.Length && plain[index] == ' ')
		{
			index++;
		}

		return index;
	}
}

/// <summary>
/// The arguments of one command dispatch, and whether any of them failed.
/// </summary>
/// <remarks>
/// Each dispatch owns its arguments and observes only deferred evaluations actually requested
/// by that command. Raw/skipped arguments never contribute a failure, and no state is ambient.
/// </remarks>
internal sealed class CommandArguments
{
	private bool _deferredHadErrors;
	public List<CallState> Values { get; } = [];
	public bool HadErrors => _deferredHadErrors || Values.Any(argument => argument.HadErrors);

	public CallState? Record(CallState? result)
	{
		if (result?.HadErrors == true) _deferredHadErrors = true;
		return result;
	}

	/// <summary><paramref name="result"/>, marked as failed when any of these arguments failed.</summary>
	public Option<CallState> Preserve(Option<CallState> result)
		=> HadErrors
			? (result is CallState value ? value : CallState.Empty) with { HadErrors = true }
			: result;
}
