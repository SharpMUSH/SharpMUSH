using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Visitors;

/// <summary>
/// What evaluation tells a player about itself rather than computes: DEBUG and VERBOSE traces,
/// their forwarding, and the "DID YOU MEAN" hint on an unknown function.
/// </summary>
internal sealed class EvaluationDiagnostics(EvaluationServices services)
{
	/// <summary>
	/// The attribute-level DEBUG decision (PennMUSH's <c>QUEUE_DEBUG</c> / <c>QUEUE_NODEBUG</c>):
	/// <see langword="false"/> under <see cref="ParserStateFlags.NoDebug"/>, <see langword="true"/>
	/// under <see cref="ParserStateFlags.Debug"/>, and <see langword="null"/> when neither is set and
	/// the executor's own <c>DEBUG</c> flag decides.
	/// </summary>
	public static bool? DebugOverride(ParserStateFlags flags)
	{
		if (flags.HasFlag(ParserStateFlags.NoDebug))
			return false;
		if (flags.HasFlag(ParserStateFlags.Debug))
			return true;
		return null;
	}

	/// <summary>
	/// Sends debug or verbose output to owner and DEBUGFORWARDLIST recipients.
	/// </summary>
	/// <param name="parser">The parser whose evaluation is being traced.</param>
	/// <param name="executor">The executor object</param>
	/// <param name="message">The message to send</param>
	public async ValueTask SendDebugOrVerboseOutput(IMUSHCodeParser parser, AnySharpObject executor, string message)
	{
		if (EvaluationRestrictions.Current is not null || parser.CurrentState.Restrictions is not null)
			return;
		var owner = await executor.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken);
		await services.NotifyService.Notify(owner, MarkupText.Plain(message), executor);

		var debugForwardAttr = await services.AttributeService.GetAttributeAsync(
			executor, executor, "DEBUGFORWARDLIST",
			IAttributeService.AttributeMode.Read, parent: true);

		if (debugForwardAttr is not SharpAttribute[] forwardChain)
		{
			return;
		}

		var attr = forwardChain.Last();
		var forwardListText = attr.Value.ToPlainText();
		if (string.IsNullOrWhiteSpace(forwardListText))
		{
			return;
		}

		foreach (var targetStr in forwardListText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			// Forwarding is receiver-directed, like a page; the sender need not see the recipient.
			var locateResult = await services.LocateService.Locate(parser, executor, executor, targetStr,
				LocateFlags.AbsoluteMatch | LocateFlags.MatchForPage);
			if (locateResult is AnySharpObject forwardTarget)
			{
				await services.NotifyService.Notify(forwardTarget, MarkupText.Plain(message), executor);
			}
		}
	}

	/// <summary>
	/// PennMUSH substitution-only debug: when a function-position argument contains only
	/// substitutions (no function calls), emit a single-line debug trace: "#dbref! raw => evaluated".
	/// Only fires when the parse neither emitted function traces nor contains a restricted wrapper
	/// (<paramref name="suppressSubstitutionDebug"/>), and raw and evaluated text differ.
	/// <para>
	/// Shared by <see cref="MUSHCodeParser.FunctionParse(MString, bool)"/> and
	/// <see cref="CommandArgumentSplitter"/>, which evaluates a retained argument subtree directly
	/// instead of going through FunctionParse's own lex+parse pass.
	/// </para>
	/// </summary>
	/// <param name="callerState">
	/// The state to check DEBUG/NODEBUG flags and resolve the executor against — always the
	/// state of the parser that WOULD have called <see cref="MUSHCodeParser.FunctionParse(MString, bool)"/>
	/// (i.e. its current state at the call site), not any fresh tracking state pushed by
	/// <see cref="IMUSHCodeParser.ForTrackedEvaluation"/> for the evaluation itself.
	/// </param>
	public static async ValueTask EmitSubstitutionOnlyDebugTraceAsync(
		IMediator mediator,
		INotifyService notifyService,
		ParserState callerState,
		string rawText,
		MString? resultMessage,
		bool suppressSubstitutionDebug)
	{
		if (EvaluationRestrictions.Current is not null || callerState.Restrictions is not null
			|| suppressSubstitutionDebug || resultMessage is null)
		{
			return;
		}

		var evaluatedText = resultMessage.ToPlainText();
		if (rawText == evaluatedText)
		{
			return;
		}

		if (await callerState.ExecutorObject(mediator) is not AnySharpObject executorObj)
		{
			return;
		}

		var shouldDebug = DebugOverride(callerState.Flags) ?? await executorObj.HasFlag("DEBUG");

		if (shouldDebug)
		{
			var dbrefNumber = executorObj.Object().DBRef.Number;
			var owner = await executorObj.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken);
			await notifyService.Notify(owner, MarkupText.Plain($"#{dbrefNumber}! {rawText} => {evaluatedText}"), executorObj);
		}
	}

	/// <summary>
	/// The error an unknown function inside <c>[...]</c> reports: PennMUSH's <c>#-1 FUNCTION (X) NOT FOUND</c>,
	/// with its "DID YOU MEAN 'Y'" hint when a known name is close enough.
	/// </summary>
	public static string UnknownFunction(string name, LibraryService<string, FunctionDefinition> functions)
	{
		var notFound = string.Format(ErrorMessages.Returns.NoSuchFunction, name.ToUpperInvariant());
		var suggestion = SuggestFunctionName(name, functions);
		if (suggestion is not null)
		{
			notFound += $" DID YOU MEAN '{suggestion.ToUpperInvariant()}'";
		}

		return notFound;
	}

	/// <summary>
	/// The known function name closest to <paramref name="name"/>, for PennMUSH's
	/// "DID YOU MEAN 'X'" hint on an unknown function inside <c>[...]</c> (src/parse.c). Returns the
	/// nearest library name within a small edit distance — <c>min(2, max(1, len/3))</c>: 1 for names
	/// up to 5 characters, 2 for longer, never more than 2 — so short typos do not match everything,
	/// or null if nothing is close enough. Only ever suggests names that exist, so at worst a
	/// suggestion is missed, never wrong.
	/// </summary>
	public static string? SuggestFunctionName(string name,
		LibraryService<string, FunctionDefinition> functions)
	{
		var typed = name.ToLowerInvariant();
		var budget = Math.Min(2, Math.Max(1, typed.Length / 3));

		string? best = null;
		var bestDistance = int.MaxValue;
		foreach (var (candidate, _) in functions)
		{
			// FunctionLibrary is case-insensitive but does not normalise its stored keys, so a key
			// can be any case (built-ins are lower-case, but a user-registered alias need not be).
			// Lower-case it for the otherwise case-sensitive distance comparison. This is the
			// unknown-function error path, so the per-candidate allocation is not on a hot path.
			var lower = candidate.ToLowerInvariant();
			var distance = LevenshteinWithin(typed, lower, budget);
			if (distance < 0)
			{
				continue;
			}

			// Closest wins; ties break alphabetically for a stable suggestion.
			if (distance < bestDistance || (distance == bestDistance && (best is null || string.CompareOrdinal(lower, best) < 0)))
			{
				best = lower;
				bestDistance = distance;
			}
		}

		return best;
	}

	/// <summary>
	/// Levenshtein edit distance between <paramref name="a"/> and <paramref name="b"/>, returning
	/// -1 as soon as it is known to exceed <paramref name="max"/> (a length gap alone can decide
	/// this without any work). Bounding the search keeps the per-candidate cost tiny.
	/// </summary>
	private static int LevenshteinWithin(string a, string b, int max)
	{
		if (Math.Abs(a.Length - b.Length) > max)
		{
			return -1;
		}

		var previous = new int[b.Length + 1];
		var current = new int[b.Length + 1];
		for (var j = 0; j <= b.Length; j++)
		{
			previous[j] = j;
		}

		for (var i = 1; i <= a.Length; i++)
		{
			current[0] = i;
			var rowMin = current[0];
			for (var j = 1; j <= b.Length; j++)
			{
				var cost = a[i - 1] == b[j - 1] ? 0 : 1;
				current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
				rowMin = Math.Min(rowMin, current[j]);
			}

			if (rowMin > max)
			{
				return -1;
			}

			(previous, current) = (current, previous);
		}

		return previous[b.Length] <= max ? previous[b.Length] : -1;
	}
}
