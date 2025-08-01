using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "and", Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> And(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(new(
			parser.CurrentState.Arguments.Select(x => x.Value.Message!).All(Predicates.Truthy)
				? "1"
				: "0"));

	[SharpFunction(Name = "cand", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
	public static async ValueTask<CallState> CancellingAnd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> new(await parser.CurrentState.Arguments
			.Select(x => x.Value.Message!)
			.ToAsyncEnumerable()
			.AllAwaitAsync(async m => (await parser.FunctionParse(m))!.Message.Truthy())
			? "0"
			: "1");

	[SharpFunction(Name = "cor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
	public static async ValueTask<CallState> CancellingOr(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> new(await parser.CurrentState.Arguments
			.Select(x => x.Value.Message!)
			.ToAsyncEnumerable()
			.AllAwaitAsync(async m => (await parser.FunctionParse(m))!.Message.Truthy())
			? "1"
			: "0");

	[SharpFunction(Name = "eq", Flags = FunctionFlags.Regular | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Equals(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(new(
			parser.CurrentState.Arguments.All(x => x.Value.Message?.ToPlainText() == parser.CurrentState.Arguments["0"].Message?.ToPlainText())
				? "1"
				: "0"));

	[SharpFunction(Name = "gt", MinArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> GreaterThan(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValidateDecimalAndEvaluatePairwise(parser.CurrentState.Arguments, pair => pair.Item1 > pair.Item2);

	[SharpFunction(Name = "gte", MinArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> GreaterThanOrEquals(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValidateDecimalAndEvaluatePairwise(parser.CurrentState.Arguments, pair => pair.Item1 >= pair.Item2);

	[SharpFunction(Name = "lt", MinArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> LessThan(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValidateDecimalAndEvaluatePairwise(parser.CurrentState.Arguments, pair => pair.Item1 < pair.Item2);

	[SharpFunction(Name = "lte", MinArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> LessThanOrEquals(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValidateDecimalAndEvaluatePairwise(parser.CurrentState.Arguments, pair => pair.Item1 <= pair.Item2);

	[SharpFunction(Name = "nand", Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> NegativeAnd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(
			new(parser.CurrentState.Arguments.Select(x => x.Value.Message!).Any(Predicates.Falsy) ? "1" : "0"));

	[SharpFunction(Name = "cnand", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
	public static async ValueTask<CallState> CancellingNegativeAnd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		foreach (var m in parser.CurrentState.Arguments.Select(x => x.Value.Message!))
		{
			var parsed = await parser.FunctionParse(m);
			if (parsed!.Message.Falsy())
			{
				return new CallState("1");
			}
		}

		return new CallState("0");
	}

	[SharpFunction(Name = "neq", Flags = FunctionFlags.Regular | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Neq(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(new(
			parser.CurrentState.Arguments.Any(x => x.Value.Message == parser.CurrentState.Arguments["0"].Message)
				? "0"
				: "1"));

	[SharpFunction(Name = "nor", Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Nor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(
			new(parser.CurrentState.Arguments.Select(x => x.Value.Message!).All(Predicates.Falsy) ? "1" : "0"));

	[SharpFunction(Name = "ncor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
	public static async ValueTask<CallState> NCor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> new(await parser.CurrentState.Arguments
			.Select(x => x.Value.Message!)
			.ToAsyncEnumerable()
			.AllAwaitAsync(async m => (await parser.FunctionParse(m))!.Message.Falsy())
			? "1"
			: "0");

	[SharpFunction(Name = "not", Flags = FunctionFlags.Regular, MinArgs = 1, MaxArgs = 1)]
	public static ValueTask<CallState> Not(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(new(parser.CurrentState.Arguments.First().Value.Message.Falsy()
			? "1"
			: "0"));

	[SharpFunction(Name = "or", Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Or(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(
			new(parser.CurrentState.Arguments.Select(x => x.Value.Message!).Any(Predicates.Truthy) ? "1" : "0"));

	[SharpFunction(Name = "t", Flags = FunctionFlags.Regular, MinArgs = 0, MaxArgs = 1)]
	public static ValueTask<CallState> T(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(
			new(parser.CurrentState.Arguments.FirstOrDefault().Value.Message.Truthy() 
				? "1" 
				: "0"));

	[SharpFunction(Name = "xor", Flags = FunctionFlags.Regular | FunctionFlags.NoParse)]
	public static ValueTask<CallState> Xor(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult<CallState>(new(
			parser.CurrentState.Arguments.Select(x => x.Value.Message!).Where(Predicates.Truthy).Count() == 1 ? "1" : "0"));
}