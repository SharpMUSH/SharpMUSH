using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using DotNext.Collections.Generic;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using System.Text.RegularExpressions;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// SharpMUSH Implementation Status: 100%
/// </summary>
public partial class Functions
{
	[SharpFunction(Name = "and", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["boolean..."])]
	public ValueTask<CallState> And(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments
			.Select(x => x.Value.Message!)
			.All(value => value.Truthy(parser))
			? "1"
			: "0");

	[SharpFunction(Name = "cand", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.NoParse, ParameterNames = ["boolean..."])]
	public ValueTask<CallState> CancellingAnd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> EvaluateLazyBoolean(parser, parser.CurrentState.Arguments.Values, all: true, truthy: true);

	[SharpFunction(Name = "cor", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.NoParse, ParameterNames = ["boolean..."])]
	public ValueTask<CallState> CancellingOr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> EvaluateLazyBoolean(parser, parser.CurrentState.Arguments.Values, all: false, truthy: true);

	private static async ValueTask<CallState> EvaluateLazyBoolean(IMUSHCodeParser parser,
		IEnumerable<CallState> arguments, bool all, bool truthy)
	{
		var hadErrors = false;
		async ValueTask<bool> Test(CallState argument, CancellationToken _)
		{
			var parsed = await parser.FunctionParse(argument.Message!) ?? CallState.Empty;
			hadErrors |= parsed.HadErrors;
			return truthy ? parsed.Message.Truthy(parser) : parsed.Message.Falsy(parser);
		}
		var values = arguments.ToAsyncEnumerable();
		var result = all ? await values.AllAsync(Test) : await values.AnyAsync(Test);
		return new CallState(result) { HadErrors = hadErrors };
	}

	[SharpFunction(Name = "eq", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.DecimalsOnly, ParameterNames = ["value..."])]
	public ValueTask<CallState> ExactEquals(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.ValidateDecimalAndEvaluatePairwise(parser, pair => pair.Item1 == pair.Item2);

	[SharpFunction(Name = "gt", MinArgs = 2, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["value..."])]
	public ValueTask<CallState> GreaterThan(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.ValidateDecimalAndEvaluatePairwise(parser, pair => pair.Item1 > pair.Item2);

	[SharpFunction(Name = "gte", MinArgs = 2, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["value..."])]
	public ValueTask<CallState> GreaterThanOrEquals(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.ValidateDecimalAndEvaluatePairwise(parser, pair => pair.Item1 >= pair.Item2);

	[SharpFunction(Name = "lt", MinArgs = 2, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["value..."])]
	public ValueTask<CallState> LessThan(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.ValidateDecimalAndEvaluatePairwise(parser, pair => pair.Item1 < pair.Item2);

	[SharpFunction(Name = "lte", MinArgs = 2, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["value..."])]
	public ValueTask<CallState> LessThanOrEquals(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.ValidateDecimalAndEvaluatePairwise(parser, pair => pair.Item1 <= pair.Item2);

	[SharpFunction(Name = "nand", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["boolean..."])]
	public ValueTask<CallState> NegativeAnd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.ArgumentsOrdered
			.Select(x => x.Value.Message!)
			.Any(value => value.Falsy(parser))
			? "1"
			: "0");

	// PennMUSH registers no CNAND; SharpMUSH offers it as a second spelling of NCAND
	// ({"NCAND", fun_cand, 1, INT_MAX, …}, function.c:385) and so declares NCAND's arity.
	[SharpFunction(Name = "cnand", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.NoParse, ParameterNames = ["boolean..."])]
	public ValueTask<CallState> CancellingNegativeAnd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> EvaluateLazyBoolean(parser, parser.CurrentState.ArgumentsOrdered.Values, all: false, truthy: false);

	[SharpFunction(Name = "ncand", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.NoParse, ParameterNames = ["boolean..."])]
	public ValueTask<CallState> NCand(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> CancellingNegativeAnd(parser, _2);

	[SharpFunction(Name = "neq", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.DecimalsOnly, ParameterNames = ["value..."])]
	public ValueTask<CallState> Neq(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.ValidateDecimalAndEvaluatePairwise(parser,
			pair => pair.Item1 == pair.Item2, negate: true);

	[SharpFunction(Name = "nor", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["boolean..."])]
	public ValueTask<CallState> Nor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments
			.Select(x => x.Value.Message!)
			.All(value => value.Falsy(parser))
			? "1"
			: "0");

	[SharpFunction(Name = "ncor", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.NoParse, ParameterNames = ["boolean..."])]
	public ValueTask<CallState> NCor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> EvaluateLazyBoolean(parser, parser.CurrentState.Arguments.Values, all: true, truthy: false);

	[SharpFunction(Name = "not", Flags = FunctionFlags.Regular, MinArgs = 1, MaxArgs = 1, ParameterNames = ["boolean"])]
	public ValueTask<CallState> Not(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments.First().Value.Message.Falsy(parser)
			? "1"
			: "0");

	[SharpFunction(Name = "or", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["boolean..."])]
	public ValueTask<CallState> Or(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments
			.Select(x => x.Value.Message!)
			.Any(value => value.Truthy(parser))
			? "1"
			: "0");

	[SharpFunction(Name = "t", Flags = FunctionFlags.Regular, MinArgs = 1, MaxArgs = 1, ParameterNames = ["value"])]
	public ValueTask<CallState> T(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments
			.FirstOrDefault().Value.Message.Truthy(parser)
			? "1"
			: "0");

	[SharpFunction(Name = "xor", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["boolean..."])]
	public ValueTask<CallState> Xor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments
			.Select(x => x.Value.Message!)
			.Count(value => value.Truthy(parser)) == 1
			? "1"
			: "0");

	[SharpFunction(Name = "isdbref", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public async ValueTask<CallState> IsDbRef(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (HelperFunctions.ParseDbRef((parser.CurrentState.Arguments["0"].Message ?? MarkupText.Empty).ToPlainText()) is not DBRef dbref) return new("0");
		return new CallState(await Mediator.Send(new GetObjectNodeQuery(dbref)) is AnySharpObject);
	}

	/// <summary>
	/// 64-bit, as <c>fun_isint</c> is (<c>parse_ival_full</c> = <c>parse_int64</c>,
	/// <c>src/funmath.c:82-84</c>). A 32-bit parse said no to every value above 2147483647 that
	/// <c>add()</c>, <c>sub()</c> and <c>div()</c> all handled as an integer.
	/// </summary>
	[SharpFunction(Name = "isint", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> IsInt(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ValueTask.FromResult<CallState>(new(long.TryParse(parser.CurrentState.Arguments["0"].Message!.ToString(), out var _) ? "1" : "0"));

	[SharpFunction(Name = "isnum", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> IsNum(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ValueTask.FromResult<CallState>(new(decimal.TryParse(parser.CurrentState.Arguments["0"].Message!.ToString(), out var _) ? "1" : "0"));

	[SharpFunction(Name = "isobjid", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> IsObjId(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg = (parser.CurrentState.Arguments["0"].Message ?? MarkupText.Empty).ToPlainText();
		// Object ID format is #dbref:timestamp (e.g., #123:456789)
		var match = ObjIdRegex().Match(arg);
		return ValueTask.FromResult(new CallState(match.Success ? "1" : "0"));
	}

	[GeneratedRegex(@"^#\d+:\d+$")]
	private static partial Regex ObjIdRegex();

	[SharpFunction(Name = "isregexp", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> isregexp(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (string.IsNullOrWhiteSpace(arg)) return ValueTask.FromResult<CallState>(new("0"));

		// Validate regex by attempting to construct it with timeout to prevent ReDoS
		// Use a helper method to avoid exception-based control flow
		var isValid = IsValidRegexPattern(arg);
		return ValueTask.FromResult<CallState>(new(isValid ? "1" : "0"));
	}

	private bool IsValidRegexPattern(string pattern)
	{
		try
		{
			// Use a timeout to prevent catastrophic backtracking (ReDoS)
			// This is a validation step, not control flow - we're checking validity
			_ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
			return true;
		}
		catch (ArgumentException)
		{
			return false;
		}
		catch (RegexMatchTimeoutException)
		{
			return false;
		}
	}

	[SharpFunction(Name = "isword", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public ValueTask<CallState> IsWord(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var str = (parser.CurrentState.Arguments["0"].Message ?? MarkupText.Empty).ToPlainText();
		return ValueTask.FromResult(new CallState(IsWordRegex().IsMatch(str)));
	}

	[GeneratedRegex(@"^[a-zA-Z]+$")]
	private static partial Regex IsWordRegex();
}