using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// SharpMUSH Implementation Status: 100%
/// </summary>
public partial class Functions
{
	[SharpFunction(Name = "and", Flags = FunctionFlags.Regular, ParameterNames = ["boolean..."])]
	public ValueTask<CallState> And(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments
			.Select(x => x.Value.Message!)
			.All(Predicates.Truthy)
			? "1"
			: "0");

	[SharpFunction(Name = "cand", Flags = FunctionFlags.Regular | FunctionFlags.NoParse, ParameterNames = ["boolean..."])]
	public async ValueTask<CallState> CancellingAnd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await parser.CurrentState.Arguments
			.Select(x => x.Value.Message!)
			.ToAsyncEnumerable()
			.AllAsync(async (m, _) => (await parser.FunctionParse(m))!.Message.Truthy())
			? "1"
			: "0";

	[SharpFunction(Name = "cor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse, ParameterNames = ["boolean..."])]
	public async ValueTask<CallState> CancellingOr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await parser.CurrentState.Arguments
			.Select(x => x.Value.Message!)
			.ToAsyncEnumerable()
			.AnyAsync(async (m, _) => (await parser.FunctionParse(m))!.Message.Truthy())
			? "1"
			: "0";

	[SharpFunction(Name = "eq", Flags = FunctionFlags.Regular | FunctionFlags.DecimalsOnly, ParameterNames = ["value1", "value2"])]
	public ValueTask<CallState> ExactEquals(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.ValidateDecimalAndEvaluatePairwise(parser.CurrentState.ArgumentsOrdered, pair => pair.Item1 == pair.Item2);

	[SharpFunction(Name = "gt", MinArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["value1", "value2"])]
	public ValueTask<CallState> GreaterThan(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.ValidateDecimalAndEvaluatePairwise(parser.CurrentState.ArgumentsOrdered, pair => pair.Item1 > pair.Item2);

	[SharpFunction(Name = "gte", MinArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["value1", "value2"])]
	public ValueTask<CallState> GreaterThanOrEquals(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.ValidateDecimalAndEvaluatePairwise(parser.CurrentState.ArgumentsOrdered, pair => pair.Item1 >= pair.Item2);

	[SharpFunction(Name = "lt", MinArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["value1", "value2"])]
	public ValueTask<CallState> LessThan(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.ValidateDecimalAndEvaluatePairwise(parser.CurrentState.ArgumentsOrdered, pair => pair.Item1 < pair.Item2);

	[SharpFunction(Name = "lte", MinArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["value1", "value2"])]
	public ValueTask<CallState> LessThanOrEquals(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.ValidateDecimalAndEvaluatePairwise(parser.CurrentState.ArgumentsOrdered, pair => pair.Item1 <= pair.Item2);

	[SharpFunction(Name = "nand", Flags = FunctionFlags.Regular, ParameterNames = ["boolean..."])]
	public ValueTask<CallState> NegativeAnd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.ArgumentsOrdered
			.Select(x => x.Value.Message!)
			.Any(Predicates.Falsy)
			? "1"
			: "0");

	[SharpFunction(Name = "cnand", Flags = FunctionFlags.Regular | FunctionFlags.NoParse, ParameterNames = ["boolean1", "boolean2"])]
	public async ValueTask<CallState> CancellingNegativeAnd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		foreach (var m in parser.CurrentState.ArgumentsOrdered.Select(x => x.Value.Message!))
		{
			var parsed = await parser.FunctionParse(m);

			if (parsed!.Message.Falsy())
			{
				return "1";
			}
		}

		return "0";
	}

	[SharpFunction(Name = "neq", Flags = FunctionFlags.Regular | FunctionFlags.DecimalsOnly, ParameterNames = ["value1", "value2"])]
	public ValueTask<CallState> Neq(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments
			.Any(x => x.Value.Message!.ToPlainText() == parser.CurrentState.Arguments["0"].Message!.ToPlainText())
			? "0"
			: "1");

	[SharpFunction(Name = "nor", Flags = FunctionFlags.Regular, ParameterNames = ["boolean..."])]
	public ValueTask<CallState> Nor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments
			.Select(x => x.Value.Message!)
			.All(Predicates.Falsy)
			? "1"
			: "0");

	[SharpFunction(Name = "ncor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse, ParameterNames = ["boolean1", "boolean2"])]
	public async ValueTask<CallState> NCor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await parser.CurrentState.Arguments
			.Select(x => x.Value.Message!)
			.ToAsyncEnumerable()
			.AllAsync(async (m, _) => (await parser.FunctionParse(m))!.Message.Falsy())
			? "1"
			: "0";

	[SharpFunction(Name = "not", Flags = FunctionFlags.Regular, MinArgs = 1, MaxArgs = 1, ParameterNames = ["boolean"])]
	public ValueTask<CallState> Not(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments.First().Value.Message.Falsy()
			? "1"
			: "0");

	[SharpFunction(Name = "or", Flags = FunctionFlags.Regular, ParameterNames = ["boolean..."])]
	public ValueTask<CallState> Or(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments
			.Select(x => x.Value.Message!)
			.Any(Predicates.Truthy)
			? "1"
			: "0");

	[SharpFunction(Name = "t", Flags = FunctionFlags.Regular, MinArgs = 0, MaxArgs = 1, ParameterNames = ["value"])]
	public ValueTask<CallState> T(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments
			.FirstOrDefault().Value.Message.Truthy()
			? "1"
			: "0");

	[SharpFunction(Name = "xor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse, ParameterNames = ["boolean..."])]
	public ValueTask<CallState> Xor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(parser.CurrentState.Arguments
			.Select(x => x.Value.Message!)
			.Where(Predicates.Truthy)
			.Count() == 1
			? "1"
			: "0");
}