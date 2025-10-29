using System.Globalization;
using System.Numerics;
using MoreLinq;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "add", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Add(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ArgHelpers.AggregateDecimals(parser.CurrentState.ArgumentsOrdered, (acc, sub) => acc + sub);

	[SharpFunction(Name = "sub", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Sub(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ArgHelpers.AggregateDecimals(parser.CurrentState.ArgumentsOrdered, (acc, sub) => acc - sub);

	[SharpFunction(Name = "mul", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Mul(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ArgHelpers.AggregateDecimals(parser.CurrentState.ArgumentsOrdered, (acc, sub) => acc * sub);

	[SharpFunction(Name = "div", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.IntegersOnly)]
	public static ValueTask<CallState> Div(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		parser.CurrentState.Arguments.Skip(1).Any(x
				=> decimal.TryParse(MModule.plainText(x.Value.Message), out var num) && num == 0)
			? ValueTask.FromResult(new CallState(Errors.ErrorDivideByZero))
			: ArgHelpers.AggregateIntegers(parser.CurrentState.ArgumentsOrdered, (acc, sub) => acc / sub);

	[SharpFunction(Name = "fdiv", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> FDiv(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ArgHelpers.AggregateDecimals(parser.CurrentState.ArgumentsOrdered, (acc, sub) => acc / sub);

	[SharpFunction(Name = "floordiv",
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> FloorDiv(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ArgHelpers.AggregateDecimalToInt(parser.CurrentState.ArgumentsOrdered, (acc, sub) => acc / sub);

	[SharpFunction(Name = "max", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Max(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ArgHelpers.AggregateDecimals(parser.CurrentState.ArgumentsOrdered, Math.Max);

	[SharpFunction(Name = "min", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Min(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ArgHelpers.AggregateDecimals(parser.CurrentState.ArgumentsOrdered, Math.Min);

	[SharpFunction(Name = "abs", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Abs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.EvaluateDecimal(parser.CurrentState.ArgumentsOrdered, Math.Abs);

	[SharpFunction(Name = "bound", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Bound(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		try
		{
			var value = decimal.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)));
			var min = decimal.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["1"].Message)));
			
			if (args.Count == 2)
			{
				// If only 2 args, just return max(value, min)
				return ValueTask.FromResult<CallState>(Math.Max(value, min));
			}
			
			var max = decimal.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["2"].Message)));
			
			// Clamp the value between min and max
			return ValueTask.FromResult<CallState>(Math.Clamp(value, min, max));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumbers);
		}
	}

	[SharpFunction(Name = "dec", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Dec(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.EvaluateDecimal(parser.CurrentState.ArgumentsOrdered, x => x - 1);

	[SharpFunction(Name = "decode64", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Decode64(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
		var args = parser.CurrentState.ArgumentsOrdered;
		try
		{
			var x1 = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)));
			var y1 = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["1"].Message)));
			var x2 = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["2"].Message)));
			var y2 = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["3"].Message)));
			
			var distance = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
			return ValueTask.FromResult<CallState>(distance);
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumbers);
		}
	}

	[SharpFunction(Name = "dist3d", MinArgs = 6, MaxArgs = 6, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Distance3d(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		try
		{
			var x1 = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)));
			var y1 = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["1"].Message)));
			var z1 = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["2"].Message)));
			var x2 = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["3"].Message)));
			var y2 = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["4"].Message)));
			var z2 = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["5"].Message)));
			
			var distance = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2) + Math.Pow(z2 - z1, 2));
			return ValueTask.FromResult<CallState>(distance);
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumbers);
		}
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
		var args = parser.CurrentState.ArgumentsOrdered;
		try
		{
			var value = decimal.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)));
			var showWhole = args.Count == 2 && int.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["1"].Message))) != 0;
			
			var wholePart = Math.Truncate(value);
			var fractionalPart = Math.Abs(value - wholePart);
			
			// If it's a whole number, just return it
			if (fractionalPart < 0.000001m)
			{
				return ValueTask.FromResult<CallState>(((int)wholePart).ToString());
			}
			
			// Convert fractional part to a fraction using Fractions library
			var fraction = Fractions.Fraction.FromDecimal(fractionalPart);
			
			// Adjust for negative numbers
			var numerator = fraction.Numerator;
			var denominator = fraction.Denominator;
			
			if (value < 0 && wholePart == 0)
			{
				numerator = -numerator;
			}
			
			// If we have a whole part and showWhole is true
			if (Math.Abs(wholePart) >= 1 && showWhole)
			{
				return ValueTask.FromResult<CallState>($"{(int)wholePart} {numerator}/{denominator}");
			}
			// If we have a whole part but showWhole is false, add it to the fraction
			else if (Math.Abs(wholePart) >= 1 && !showWhole)
			{
				numerator += (long)wholePart * denominator;
				return ValueTask.FromResult<CallState>($"{numerator}/{denominator}");
			}
			// Just the fraction
			else
			{
				return ValueTask.FromResult<CallState>($"{numerator}/{denominator}");
			}
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumbers);
		}
	}

	[SharpFunction(Name = "inc", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Inc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.EvaluateDecimal(parser.CurrentState.ArgumentsOrdered, x => x + 1);

	[SharpFunction(Name = "lmath", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> LMath(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var args = parser.CurrentState.ArgumentsOrdered;
		
		var operation = MModule.plainText(args["0"].Message).ToLower();
		var delimiter = args.Count == 3 ? args["2"].Message : MModule.single(" ");
		var list = MModule.split2(delimiter, args["1"].Message).ToList();
		
		try
		{
			var values = list.Select(x => decimal.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(x)))).ToList();
			
			if (values.Count == 0)
			{
				return new CallState("0");
			}
			
			// Check for division by zero for operations that divide
			if ((operation == "div" || operation == "fdiv" || operation == "modulo" || operation == "remainder") 
			    && values.Skip(1).Any(v => v == 0))
			{
				return Errors.ErrorDivideByZero;
			}
			
			string result = operation switch
			{
				// Arithmetic operations
				"add" => values.Sum().ToString(CultureInfo.InvariantCulture),
				"sub" => values.Aggregate((acc, val) => acc - val).ToString(CultureInfo.InvariantCulture),
				"mul" => values.Aggregate((acc, val) => acc * val).ToString(CultureInfo.InvariantCulture),
				"div" => values.Aggregate((acc, val) => acc / val).ToString(CultureInfo.InvariantCulture),
				"fdiv" => values.Aggregate((acc, val) => acc / val).ToString(CultureInfo.InvariantCulture),
				"modulo" => values.Aggregate((acc, val) => acc % val).ToString(CultureInfo.InvariantCulture),
				"remainder" => values.Aggregate((acc, val) => acc % val).ToString(CultureInfo.InvariantCulture),
				
				// Comparison operations
				"max" => values.Max().ToString(CultureInfo.InvariantCulture),
				"min" => values.Min().ToString(CultureInfo.InvariantCulture),
				"eq" => (values.All(v => v == values[0]) ? 1 : 0).ToString(),
				"neq" => (values.Zip(values.Skip(1), (a, b) => a != b).All(x => x) ? 1 : 0).ToString(),
				"gt" => (values.Zip(values.Skip(1), (a, b) => a > b).All(x => x) ? 1 : 0).ToString(),
				"gte" => (values.Zip(values.Skip(1), (a, b) => a >= b).All(x => x) ? 1 : 0).ToString(),
				"lt" => (values.Zip(values.Skip(1), (a, b) => a < b).All(x => x) ? 1 : 0).ToString(),
				"lte" => (values.Zip(values.Skip(1), (a, b) => a <= b).All(x => x) ? 1 : 0).ToString(),
				
				// Logical operations (treat non-zero as true)
				"and" => (values.All(v => v != 0) ? 1 : 0).ToString(),
				"or" => (values.Any(v => v != 0) ? 1 : 0).ToString(),
				"xor" => (values.Count(v => v != 0) == 1 ? 1 : 0).ToString(),
				"nand" => (!values.All(v => v != 0) ? 1 : 0).ToString(),
				"nor" => (!values.Any(v => v != 0) ? 1 : 0).ToString(),
				
				// Bitwise operations (convert to int)
				"band" => values.Select(v => (int)v).Aggregate((acc, val) => acc & val).ToString(),
				"bor" => values.Select(v => (int)v).Aggregate((acc, val) => acc | val).ToString(),
				"bxor" => values.Select(v => (int)v).Aggregate((acc, val) => acc ^ val).ToString(),
				
				// Statistical operations
				"mean" => values.Average().ToString(CultureInfo.InvariantCulture),
				"median" => CalculateMedian(values).ToString(CultureInfo.InvariantCulture),
				"stddev" => CalculateStdDev(values).ToString(CultureInfo.InvariantCulture),
				
				// Distance operations (requires exactly 4 or 6 values)
				"dist2d" when values.Count == 4 
					=> ((decimal)Math.Sqrt((double)((values[2] - values[0]) * (values[2] - values[0]) + (values[3] - values[1]) * (values[3] - values[1])))).ToString(CultureInfo.InvariantCulture),
				"dist2d" => Errors.ErrorBadArgumentFormat.Replace("{0}", "lmath"),
				"dist3d" when values.Count == 6
					=> ((decimal)Math.Sqrt((double)((values[3] - values[0]) * (values[3] - values[0]) + (values[4] - values[1]) * (values[4] - values[1]) + (values[5] - values[2]) * (values[5] - values[2])))).ToString(CultureInfo.InvariantCulture),
				"dist3d" => Errors.ErrorBadArgumentFormat.Replace("{0}", "lmath"),
				
				_ => Errors.ErrorBadArgumentFormat.Replace("{0}", "lmath")
			};
			
			return new CallState(result);
		}
		catch (Exception)
		{
			return Errors.ErrorNumbers;
		}
	}
	
	private static decimal CalculateMedian(List<decimal> values)
	{
		var sorted = values.OrderBy(x => x).ToList();
		int count = sorted.Count;
		if (count % 2 == 1)
		{
			return sorted[count / 2];
		}
		else
		{
			return (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0m;
		}
	}
	
	private static decimal CalculateStdDev(List<decimal> values)
	{
		var mean = values.Average();
		var sumOfSquaredDifferences = values.Select(val => (val - mean) * (val - mean)).Sum();
		var variance = sumOfSquaredDifferences / values.Count;
		return (decimal)Math.Sqrt((double)variance);
	}

	[SharpFunction(Name = "lnum", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> LNum(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await Task.CompletedTask;
		// lnum(<start number>, <end number>[, <output separator>[, <step>]])
		var args = parser.CurrentState.ArgumentsOrdered;

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

		var delim = ArgHelpers.NoParseDefaultNoParseArgument(args, 2, " ");
		var args3 = ArgHelpers.NoParseDefaultNoParseArgument(args, 3, "1");

		if (!int.TryParse(MModule.plainText(args3), out var arg3Val))
		{
			return new CallState(Errors.ErrorInteger);
		}

		return new CallState(string.Join(
			MModule.plainText(delim),
			Enumerable.Range(arg0Val, arg1Val - arg0Val + 1).TakeEvery(arg3Val)));
	}

	[SharpFunction(Name = "mean", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Mean(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		try
		{
			var values = args.Select(x => double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(x.Value.Message)))).ToList();
			var mean = values.Average();
			return ValueTask.FromResult<CallState>(mean);
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumbers);
		}
	}

	[SharpFunction(Name = "median", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Median(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		try
		{
			var values = args.Select(x => double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(x.Value.Message))))
				.OrderBy(x => x)
				.ToList();
			
			var count = values.Count;
			if (count == 0)
			{
				return ValueTask.FromResult<CallState>(0);
			}
			
			if (count % 2 == 1)
			{
				// Odd number of elements, return middle element
				return ValueTask.FromResult<CallState>(values[count / 2]);
			}
			else
			{
				// Even number of elements, return average of two middle elements
				var median = (values[count / 2 - 1] + values[count / 2]) / 2.0;
				return ValueTask.FromResult<CallState>(median);
			}
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumbers);
		}
	}

	[SharpFunction(Name = "modulo", MinArgs = 2, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Modulo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (parser.CurrentState.Arguments.Skip(1).Any(x => decimal.TryParse(MModule.plainText(x.Value.Message), out var num) && num == 0))
		{
			return ValueTask.FromResult(new CallState(Errors.ErrorDivideByZero));
		}
		return ArgHelpers.AggregateIntegers(parser.CurrentState.ArgumentsOrdered, (acc, mod) => acc % mod);
	}

	[SharpFunction(Name = "remainder", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Remainder(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		try
		{
			if (args.Skip(1).Any(x => double.TryParse(MModule.plainText(x.Value.Message), out var num) && num == 0))
			{
				return ValueTask.FromResult(new CallState(Errors.ErrorDivideByZero));
			}
			
			var result = args
				.Select(x => double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(x.Value.Message))))
				.Aggregate((acc, val) => acc % val);
			
			return ValueTask.FromResult<CallState>(result);
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumbers);
		}
	}

	[SharpFunction(Name = "root", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Root(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		try
		{
			var value = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)));
			var root = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["1"].Message)));
			
			return ValueTask.FromResult<CallState>(Math.Pow(value, 1.0 / root));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumbers);
		}
	}

	[SharpFunction(Name = "sign", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Sign(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.EvaluateDecimalToInteger(parser.CurrentState.ArgumentsOrdered, Math.Sign);

	[SharpFunction(Name = "trunc", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Truncate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.EvaluateDecimalToInteger(parser.CurrentState.ArgumentsOrdered, x => (int)Math.Truncate(x));

	[SharpFunction(Name = "acos", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ACos(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var angleArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var angleType = parser.CurrentState.Arguments.TryGetValue("1", out var value)
			? value.Message!.ToPlainText()
			: null;
		
		if (!double.TryParse(angleArg, out var angle))
		{
			return Errors.ErrorNumber;
		}

		return AngleTypeMath(angleType, angle, Math.Acos);
	}

	[SharpFunction(Name = "asin", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ASin(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var angleArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var angleType = parser.CurrentState.Arguments.TryGetValue("1", out var value)
			? value.Message!.ToPlainText()
			: null;
		
		if (!double.TryParse(angleArg, out var angle))
		{
			return Errors.ErrorNumber;
		}

		return AngleTypeMath(angleType, angle, Math.Asin);
	}

	[SharpFunction(Name = "atan", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ATan(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var angleArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var angleType = parser.CurrentState.Arguments.TryGetValue("1", out var value)
			? value.Message!.ToPlainText()
			: null;
		
		if (!double.TryParse(angleArg, out var angle))
		{
			return Errors.ErrorNumber;
		}

		return AngleTypeMath(angleType, angle, Math.Atan);
	}

	[SharpFunction(Name = "atan2", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> ATan2(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var args = parser.CurrentState.ArgumentsOrdered;
		try
		{
			var y = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)));
			var x = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["1"].Message)));
			
			var angleType = args.Count == 3 ? MModule.plainText(args["2"].Message) : null;
			
			return AngleTypeMath(angleType, Math.Atan2(y, x), angle => angle);
		}
		catch (Exception)
		{
			return Errors.ErrorNumbers;
		}
	}

	[SharpFunction(Name = "ceil", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Ceil(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.EvaluateDouble(parser.CurrentState.ArgumentsOrdered, Math.Ceiling);

	[SharpFunction(Name = "cos", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Cos(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var angleArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var angleType = parser.CurrentState.Arguments.TryGetValue("1", out var value)
			? value.Message!.ToPlainText()
			: null;
		
		if (!double.TryParse(angleArg, out var angle))
		{
			return Errors.ErrorNumber;
		}

		return AngleTypeMath(angleType, angle, Math.Cos);
	}

	[SharpFunction(Name = "ctu", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> CTU(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "e", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
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
		var args = parser.CurrentState.ArgumentsOrdered;
		try
		{
			var arg0 = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)));
			var arg1 = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["1"].Message)));
			
			if (arg1 == 0)
			{
				return ValueTask.FromResult(new CallState(Errors.ErrorDivideByZero));
			}
			
			return ValueTask.FromResult<CallState>(arg0 % arg1);
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumbers);
		}
	}

	[SharpFunction(Name = "floor", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Floor(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ArgHelpers.EvaluateDouble(parser.CurrentState.ArgumentsOrdered, Math.Floor);

	[SharpFunction(Name = "log", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Log(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		try
		{
			var value = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)));
			
			if (args.Count == 1)
			{
				// Base 10 logarithm
				return ValueTask.FromResult<CallState>(Math.Log10(value));
			}
			
			var baseNum = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["1"].Message)));
			return ValueTask.FromResult<CallState>(Math.Log(value, baseNum));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumbers);
		}
	}

	[SharpFunction(Name = "ln", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Ln(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ArgHelpers.EvaluateDouble(parser.CurrentState.ArgumentsOrdered, Math.Log);

	[SharpFunction(Name = "pi", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> PI(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ValueTask.FromResult<CallState>(Math.PI);

	[SharpFunction(Name = "power", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Power(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		try
		{
			var baseNum = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)));
			var exponent = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["1"].Message)));
			
			return ValueTask.FromResult<CallState>(Math.Pow(baseNum, exponent));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumbers);
		}
	}

	[SharpFunction(Name = "round", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> Round(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		try
		{
			var value = double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)));
			var decimals = int.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["1"].Message)));
			
			var rounded = Math.Round(value, decimals);
			
			if (args.Count == 3)
			{
				// Third argument indicates whether to pad with zeros
				var padZeros = int.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["2"].Message)));
				if (padZeros != 0)
				{
					return ValueTask.FromResult<CallState>(rounded.ToString($"F{decimals}"));
				}
			}
			
			return ValueTask.FromResult<CallState>(rounded);
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumbers);
		}
	}

	[SharpFunction(Name = "sin", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Sin(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var angleArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var angleType = parser.CurrentState.Arguments.TryGetValue("1", out var value)
			? value.Message!.ToPlainText()
			: null;
		
		if (!double.TryParse(angleArg, out var angle))
		{
			return Errors.ErrorNumber;
		}

		return AngleTypeMath(angleType, angle, Math.Sin);
	}

	[SharpFunction(Name = "sqrt", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
	public static ValueTask<CallState> Sqrt(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ArgHelpers.EvaluateDouble(parser.CurrentState.ArgumentsOrdered, Math.Sqrt);

	[SharpFunction(Name = "stddev", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> StdDev(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		try
		{
			var values = args.Select(x => double.Parse(ArgHelpers.EmptyStringToZero(MModule.plainText(x.Value.Message)))).ToList();
			
			if (values.Count == 0)
			{
				return ValueTask.FromResult<CallState>(0);
			}
			
			var mean = values.Average();
			var sumOfSquaredDifferences = values.Select(val => Math.Pow(val - mean, 2)).Sum();
			var variance = sumOfSquaredDifferences / values.Count;
			var stdDev = Math.Sqrt(variance);
			
			return ValueTask.FromResult<CallState>(stdDev);
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumbers);
		}
	}

	[SharpFunction(Name = "tan", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static async ValueTask<CallState> Tan(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var angleArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var angleType = parser.CurrentState.Arguments.TryGetValue("1", out var value)
			? value.Message!.ToPlainText()
			: null;
		
		if (!double.TryParse(angleArg, out var angle))
		{
			return Errors.ErrorNumber;
		}

		return AngleTypeMath(angleType, angle, Math.Tan);
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

		var vector1 = new Vector<decimal>(list1.Select(x => x.result).ToArray().AsSpan());
		var vector2 = new Vector<decimal>(list2.Select(x => x.result).ToArray().AsSpan());
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

		var output = vectorResult.ToString(CultureInfo.InvariantCulture);

		return ValueTask.FromResult<CallState>(output);
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

		var vector1 = new Vector<decimal>(list1.Select(x => x.result).ToArray().AsSpan());
		var vectorResult = func(vector1);

		var result = new decimal[list1.Length];
		vectorResult.CopyTo(result);

		var output = result.Select(x => MModule.single(x.ToString(CultureInfo.InvariantCulture)));
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

	[SharpFunction(Name = "vdim", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public static ValueTask<CallState> vdim(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(new CallState(
			MModule.split2(
				ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 1, MModule.single(" ")),
				parser.CurrentState.Arguments["0"].Message).Length.ToString()
			));
	
	private static double AngleTypeMath(string? angleType, double angle, Func<double,double> func) 
		=> angleType switch
	{
		null => func(angle),
		"g" => func(angle * (double.Pi / 200)),
		"d" => func(angle * (double.Pi / 180)),
		_ => func(angle)
	};
}