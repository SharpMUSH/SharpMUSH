using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using System.Numerics;
using SharpMUSH.Library.Definitions;
using MoreLinq;
using Antlr4.Runtime;

namespace SharpMUSH.Implementation.Functions;

public static partial class Functions
{
	[SharpFunction(Name = "add", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Add(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		AggregateDecimals(parser.CurrentState.Arguments, (acc, sub) => acc + sub);

	[SharpFunction(Name = "sub", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Sub(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		AggregateDecimals(parser.CurrentState.Arguments, (acc, sub) => acc - sub);

	[SharpFunction(Name = "mul", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Mul(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		AggregateDecimals(parser.CurrentState.Arguments, (acc, sub) => acc * sub);

	[SharpFunction(Name = "div", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.IntegersOnly)]
	public static ValueTask<CallState> Div(IMUSHCodeParser parser, SharpFunctionAttribute _2) {
		if (parser.CurrentState.Arguments.Skip(1).Any(x => decimal.TryParse(MModule.plainText(x.Value.Message), out var num) && num == 0))
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorDivideByZero));
		}
		return AggregateIntegers(parser.CurrentState.Arguments, (acc, sub) => acc / sub);
	}

	[SharpFunction(Name = "fdiv", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> FDiv(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		AggregateDecimals(parser.CurrentState.Arguments, (acc, sub) => acc / sub);

	[SharpFunction(Name = "floordiv",
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> FloorDiv(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		AggregateDecimalToInt(parser.CurrentState.Arguments, (acc, sub) => acc / sub);

	[SharpFunction(Name = "max", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Max(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		AggregateDecimals(parser.CurrentState.Arguments, Math.Max);

	[SharpFunction(Name = "min", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Min(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		AggregateDecimals(parser.CurrentState.Arguments, Math.Min);

	[SharpFunction(Name = "abs", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Abs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> EvaluateDecimal(parser.CurrentState.Arguments, Math.Abs);

	[SharpFunction(Name = "bound", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Bound(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "dec", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Dec(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "decode64", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Decode64(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "decompose", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Decompose(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "decrypt", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Decrypt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "dist2d", MinArgs = 4, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Distance2d(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "dist3d", MinArgs = 6, MaxArgs = 6, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Distance3d(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "encode64", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Encode64(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "encrypt", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Encrypt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "fraction", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Fraction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "inc", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Inc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "lmath", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> LMath(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "lnum", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> LNum(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await Task.CompletedTask;
		// lnum(<start number>, <end number>[, <output separator>[, <step>]])
		var args = parser.CurrentState.Arguments;

		if (int.TryParse(MModule.plainText(args["0"].Message), out var arg0Val))
		{
			if (args.Count == 1)
			{
				return new CallState(string.Join(" ", Enumerable.Range(0, arg0Val)));
			}
		}
		else return new CallState(Errors.ErrorInteger);

		if (!int.TryParse(MModule.plainText(args["1"].Message), out var arg1Val))
		{
			return new CallState(Errors.ErrorInteger);
		}

		var delim = NoParseDefaultNoParseArgument(args, 2, " ");
		var args3 = NoParseDefaultNoParseArgument(args, 3, "1");
		
		if (!int.TryParse(MModule.plainText(args3), out var arg3Val))
		{
			return new CallState(Errors.ErrorInteger);
		}

		return new CallState(string.Join(
			MModule.plainText(delim), 
			Enumerable.Range(arg0Val, arg1Val-arg0Val+1).TakeEvery(arg3Val)));
	}

	[SharpFunction(Name = "mean", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Mean(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "median", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Median(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "modulo", MinArgs = 2, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Modulo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
    if (parser.CurrentState.Arguments.Skip(1).Any(x => decimal.TryParse(MModule.plainText(x.Value.Message), out var num) && num == 0))
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorDivideByZero));
		}
		return AggregateIntegers(parser.CurrentState.Arguments, (acc, mod) => acc % mod);
	}

	[SharpFunction(Name = "remainder", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Remainder(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "root", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Root(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "sign", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Sign(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> EvaluateDecimalToInteger(parser.CurrentState.Arguments, Math.Sign);

	[SharpFunction(Name = "trunc", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Truncate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> EvaluateDecimalToInteger(parser.CurrentState.Arguments, x => (int)Math.Truncate(x));

	[SharpFunction(Name = "acos", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ACos(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "asin", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ASin(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "atan", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ATan(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "atan2", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> ATan2(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ceil", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Ceil(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "cos", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Cos(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ctu", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> CTU(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "e", MinArgs = 0, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> E(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arguments = parser.CurrentState.Arguments;
		var arg1 = arguments["1"]?.Message?.ToString();

		return ValueTask.FromResult<CallState>(new(double.TryParse(arg1 ?? "1", out var dec)
			? Math.Exp(dec).ToString()
			: "#-1 argument must be number"));
	}

	[SharpFunction(Name = "fmod", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> FMod(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "floor", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Floor(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		EvaluateDouble(parser.CurrentState.Arguments, Math.Floor);

	[SharpFunction(Name = "log", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Log(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ln", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Ln(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		EvaluateDouble(parser.CurrentState.Arguments, Math.Log);

	[SharpFunction(Name = "pi", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> PI(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ValueTask.FromResult<CallState>(new(Math.PI.ToString()));

	[SharpFunction(Name = "power", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Power(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "round", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Round(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "sin", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Sin(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "sqrt", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Sqrt(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		EvaluateDouble(parser.CurrentState.Arguments, Math.Sqrt);

	[SharpFunction(Name = "stddev", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> StdDev(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "tan", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Tan(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	private static ValueTask<CallState> VectorOperation(IMUSHCodeParser parser,
		Func<Vector<decimal>, Vector<decimal>, Vector<decimal>> func)
	{
		var delimiter = parser.CurrentState.Arguments.TryGetValue("3", out var tmpDelimiter)
			? tmpDelimiter.Message
			: MModule.single(" ");
		var sep = parser.CurrentState.Arguments.TryGetValue("4", out var tmpSep) ? tmpSep.Message : delimiter;
		var list1 = MModule.split2(delimiter, parser.CurrentState.Arguments["0"].Message)
			.Select(x => (decimal.TryParse(MModule.plainText(x), out var result), result)).ToArray();
		var list2 = MModule.split2(delimiter, parser.CurrentState.Arguments["1"].Message)
			.Select(x => (decimal.TryParse(MModule.plainText(x), out var result), result)).ToArray();

		if (list1.Any(x => !x.Item1) || list2.Any(x => !x.Item1))
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorNumbers));
		}

		var vector1 = new Vector<decimal>(list1.Select(x => x.Item2).ToArray().AsSpan());
		var vector2 = new Vector<decimal>(list2.Select(x => x.Item2).ToArray().AsSpan());
		var vectorResult = func(vector1, vector2);

		var result = new decimal[Math.Max(list1.Length, list2.Length)];
		vectorResult.CopyTo(result);

		var output = result.Select(x => MModule.single(x.ToString()));
		return ValueTask.FromResult(new CallState(MModule.multipleWithDelimiter(sep, output)));
	}

	private static ValueTask<CallState> VectorOperationToScalar(IMUSHCodeParser parser,
		Func<Vector<decimal>, Vector<decimal>, decimal> func)
	{
		var delimiter = parser.CurrentState.Arguments.TryGetValue("3", out var tmpDelimiter)
			? tmpDelimiter.Message
			: MModule.single(" ");
		var list1 = MModule.split2(delimiter, parser.CurrentState.Arguments["0"].Message)
			.Select(x => (decimal.TryParse(MModule.plainText(x), out var result), result)).ToArray();
		var list2 = MModule.split2(delimiter, parser.CurrentState.Arguments["1"].Message)
			.Select(x => (decimal.TryParse(MModule.plainText(x), out var result), result)).ToArray();

		if (list1.Any(x => !x.Item1) || list2.Any(x => !x.Item1))
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorNumbers));
		}

		var vector1 = new Vector<decimal>(list1.Select(x => x.result).ToArray().AsSpan());
		var vector2 = new Vector<decimal>(list2.Select(x => x.result).ToArray().AsSpan());
		var vectorResult = func(vector1, vector2);

		var output = vectorResult.ToString();

		return ValueTask.FromResult(new CallState(MModule.single(output)));
	}

	private static ValueTask<CallState> SingleVectorOperation(IMUSHCodeParser parser,
		Func<Vector<decimal>, Vector<decimal>> func)
	{
		var delimiter = parser.CurrentState.Arguments.TryGetValue("1", out var tmpDelimiter)
			? tmpDelimiter.Message
			: MModule.single(" ");
		var sep = parser.CurrentState.Arguments.TryGetValue("2", out var tmpSep) ? tmpSep.Message : delimiter;
		var list1 = MModule.split2(delimiter, parser.CurrentState.Arguments["0"].Message)
			.Select(x => (decimal.TryParse(MModule.plainText(x), out var result), result)).ToArray();

		if (list1.Any(x => !x.Item1))
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorNumbers));
		}

		var vector1 = new Vector<decimal>(list1.Select(x => x.Item2).ToArray().AsSpan());
		var vectorResult = func(vector1);

		var result = new decimal[list1.Length];
		vectorResult.CopyTo(result);

		var output = result.Select(x => MModule.single(x.ToString()));
		return ValueTask.FromResult(new CallState(MModule.multipleWithDelimiter(sep, output)));
	}

	[SharpFunction(Name = "vadd", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> VAdd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> VectorOperation(parser, Vector.Add);

	[SharpFunction(Name = "vcross", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> vcross(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> VectorOperation(parser, (v1, v2) => throw new NotImplementedException());

	[SharpFunction(Name = "vsub", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> vsub(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> VectorOperation(parser, Vector.Subtract);

	[SharpFunction(Name = "vmax", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> vmax(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> VectorOperation(parser, Vector.Max);

	[SharpFunction(Name = "vmin", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> vmin(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> VectorOperation(parser, Vector.Min);

	[SharpFunction(Name = "vmul", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> vmul(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> VectorOperation(parser, Vector.Multiply);

	[SharpFunction(Name = "vdot", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> vdot(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> VectorOperationToScalar(parser, Vector.Dot);

	[SharpFunction(Name = "vmag", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> vmag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> VectorOperationToScalar(parser, (v1, v2) => throw new NotImplementedException());

	[SharpFunction(Name = "vunit", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> vunit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> SingleVectorOperation(parser, Vector.OnesComplement);
}