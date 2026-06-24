using MoreLinq;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using System.Globalization;
using System.Numerics;
using static SharpMUSH.Library.Services.Interfaces.LocateFlags;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "add", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["number1", "number2"])]
	public static ValueTask<CallState> Add(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ArgHelpers.AggregateDecimals(parser.CurrentState.ArgumentsOrdered, (acc, sub) => acc + sub);

	[SharpFunction(Name = "sub", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["number1", "number2"])]
	public static ValueTask<CallState> Sub(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ArgHelpers.AggregateDecimals(parser.CurrentState.ArgumentsOrdered, (acc, sub) => acc - sub);

	[SharpFunction(Name = "mul", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["number1", "number2"])]
	public static ValueTask<CallState> Mul(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ArgHelpers.AggregateDecimals(parser.CurrentState.ArgumentsOrdered, (acc, sub) => acc * sub);

	[SharpFunction(Name = "div", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.IntegersOnly, ParameterNames = ["dividend", "divisor"])]
	public static ValueTask<CallState> Div(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		parser.CurrentState.Arguments.Skip(1).Any(x
				=> decimal.TryParse(MModule.plainText(x.Value.Message), out var num) && num == 0)
			? ValueTask.FromResult(new CallState(ErrorMessages.Returns.DivideByZero))
			: ArgHelpers.AggregateIntegers(parser.CurrentState.ArgumentsOrdered, (acc, sub) => acc / sub);

	[SharpFunction(Name = "fdiv", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["dividend", "divisor"])]
	public static ValueTask<CallState> FDiv(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ArgHelpers.AggregateDecimals(parser.CurrentState.ArgumentsOrdered, (acc, sub) => acc / sub);

	[SharpFunction(Name = "floordiv", MinArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["dividend", "divisor"])]
	public static ValueTask<CallState> FloorDiv(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ArgHelpers.AggregateDecimalToInt(parser.CurrentState.ArgumentsOrdered, (acc, sub) => acc / sub);

	[SharpFunction(Name = "max", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["number..."])]
	public static ValueTask<CallState> Max(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ArgHelpers.AggregateDecimals(parser.CurrentState.ArgumentsOrdered, Math.Max);

	[SharpFunction(Name = "min", MinArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["number..."])]
	public static ValueTask<CallState> Min(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ArgHelpers.AggregateDecimals(parser.CurrentState.ArgumentsOrdered, Math.Min);

	[SharpFunction(Name = "abs", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["number"])]
	public static ValueTask<CallState> Abs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.EvaluateDecimal(parser.CurrentState.ArgumentsOrdered, Math.Abs);

	[SharpFunction(Name = "bound", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["value", "min", "max"])]
	public static ValueTask<CallState> Bound(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;

		if (!decimal.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)), out var value) ||
				!decimal.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["1"].Message)), out var min))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
		}

		if (args.Count == 2)
		{
			return ValueTask.FromResult<CallState>(Math.Max(value, min));
		}

		if (!decimal.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["2"].Message)), out var max))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
		}

		return ValueTask.FromResult<CallState>(Math.Clamp(value, min, max));
	}

	[SharpFunction(Name = "dec", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number"])]
	public static ValueTask<CallState> Dec(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var text = MModule.plainText(parser.CurrentState.ArgumentsOrdered["0"].Message);

		if (int.TryParse(text, out var intVal))
		{
			return ValueTask.FromResult<CallState>((intVal - 1).ToString(CultureInfo.InvariantCulture));
		}

		if (string.IsNullOrEmpty(text))
		{
			return ValueTask.FromResult<CallState>("-1");
		}

		var lastIdx = text.Length - 1;
		if (!char.IsDigit(text[lastIdx]))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ArgMustEndInInteger));
		}

		var numStart = lastIdx;
		while (numStart > 0 && (char.IsDigit(text[numStart - 1]) || text[numStart - 1] == '-'))
		{
			if (text[numStart - 1] == '-')
			{
				numStart--;
				break;
			}
			numStart--;
		}

		var prefix = text[..numStart];
		var numPart = text[numStart..];

		if (!int.TryParse(numPart, out var num))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ArgMustEndInInteger));
		}

		return ValueTask.FromResult<CallState>($"{prefix}{num - 1}");
	}

	[SharpFunction(Name = "decode64", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["encoded-string"])]
	public static ValueTask<CallState> Decode64(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var input = parser.CurrentState.Arguments["0"].Message;
		var inputText = MModule.plainText(input);

		try
		{
			var bytes = Convert.FromBase64String(inputText);
			var decoded = System.Text.Encoding.UTF8.GetString(bytes);
			return ValueTask.FromResult<CallState>(decoded);
		}
		catch (FormatException)
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.InvalidBase64String);
		}
	}

	[SharpFunction(Name = "decrypt", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string", "password", "encoded"])]
	public static ValueTask<CallState> Decrypt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var encrypted = MModule.plainText(args["0"].Message);
		var password = MModule.plainText(args["1"].Message);
		var isEncoded = args.Count == 3 && MModule.plainText(args["2"].Message) != "0";

		if (string.IsNullOrEmpty(password))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.PasswordRequired);
		}

		try
		{
			var encryptedBytes = isEncoded
				? Convert.FromBase64String(encrypted)
				: System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(encrypted);

			var passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
			var decrypted = new byte[encryptedBytes.Length];

			for (int i = 0; i < encryptedBytes.Length; i++)
			{
				decrypted[i] = (byte)(encryptedBytes[i] ^ passwordBytes[i % passwordBytes.Length]);
			}

			return ValueTask.FromResult<CallState>(System.Text.Encoding.UTF8.GetString(decrypted));
		}
		catch (Exception ex) when (ex is FormatException || ex is ArgumentException)
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.DecryptionError);
		}
	}

	[SharpFunction(Name = "dist2d", MinArgs = 4, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["x1", "y1", "x2", "y2"])]
	public static ValueTask<CallState> Distance2d(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;

		if (!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)), out var x1) ||
				!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["1"].Message)), out var y1) ||
				!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["2"].Message)), out var x2) ||
				!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["3"].Message)), out var y2))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
		}

		var distance = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
		return ValueTask.FromResult<CallState>(distance);
	}

	[SharpFunction(Name = "dist3d", MinArgs = 6, MaxArgs = 6, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["x1", "y1", "z1", "x2", "y2", "z2"])]
	public static ValueTask<CallState> Distance3d(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;

		if (!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)), out var x1) ||
				!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["1"].Message)), out var y1) ||
				!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["2"].Message)), out var z1) ||
				!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["3"].Message)), out var x2) ||
				!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["4"].Message)), out var y2) ||
				!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["5"].Message)), out var z2))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
		}

		var distance = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2) + Math.Pow(z2 - z1, 2));
		return ValueTask.FromResult<CallState>(distance);
	}

	[SharpFunction(Name = "encode64", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public static ValueTask<CallState> Encode64(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var input = parser.CurrentState.Arguments["0"].Message;
		var inputText = MModule.plainText(input);
		var bytes = System.Text.Encoding.UTF8.GetBytes(inputText);
		var encoded = Convert.ToBase64String(bytes);
		return ValueTask.FromResult<CallState>(encoded);
	}

	[SharpFunction(Name = "encrypt", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["string", "password", "encode"])]
	public static ValueTask<CallState> Encrypt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var plaintext = MModule.plainText(args["0"].Message);
		var password = MModule.plainText(args["1"].Message);
		var shouldEncode = args.Count == 3 && MModule.plainText(args["2"].Message) != "0";

		if (string.IsNullOrEmpty(password))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.PasswordRequired);
		}

		var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
		var passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
		var encrypted = new byte[plaintextBytes.Length];

		for (int i = 0; i < plaintextBytes.Length; i++)
		{
			encrypted[i] = (byte)(plaintextBytes[i] ^ passwordBytes[i % passwordBytes.Length]);
		}

		var result = shouldEncode
			? Convert.ToBase64String(encrypted)
			: System.Text.Encoding.GetEncoding("ISO-8859-1").GetString(encrypted);

		return ValueTask.FromResult<CallState>(result);
	}

	[SharpFunction(Name = "fraction", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number", "whole"])]
	public static ValueTask<CallState> Fraction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;

		if (!decimal.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)), out var value))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
		}

		var showWhole = false;
		if (args.Count == 2)
		{
			if (!int.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["1"].Message)), out var wholeFlag))
			{
				return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
			}
			showWhole = wholeFlag != 0;
		}

		var wholePart = Math.Truncate(value);
		var fractionalPart = Math.Abs(value - wholePart);

		if (fractionalPart < 0.000001m)
		{
			return ValueTask.FromResult<CallState>(((int)wholePart).ToString());
		}

		// PennMUSH uses continued fraction approximation with a max denominator limit
		var (numerator, denominator) = ContinuedFractionApprox((double)fractionalPart, 1000000);

		if (value < 0 && wholePart == 0)
		{
			numerator = -numerator;
		}

		if (Math.Abs(wholePart) >= 1 && showWhole)
		{
			return ValueTask.FromResult<CallState>($"{(int)wholePart} {numerator}/{denominator}");
		}
		else if (Math.Abs(wholePart) >= 1 && !showWhole)
		{
			numerator += (long)wholePart * denominator;
			return ValueTask.FromResult<CallState>($"{numerator}/{denominator}");
		}
		else
		{
			return ValueTask.FromResult<CallState>($"{numerator}/{denominator}");
		}
	}

	[SharpFunction(Name = "inc", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["integer"])]
	public static ValueTask<CallState> Inc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var text = MModule.plainText(parser.CurrentState.ArgumentsOrdered["0"].Message);

		if (int.TryParse(text, out var intVal))
		{
			return ValueTask.FromResult<CallState>((intVal + 1).ToString(CultureInfo.InvariantCulture));
		}

		if (string.IsNullOrEmpty(text))
		{
			return ValueTask.FromResult<CallState>("1");
		}

		var lastIdx = text.Length - 1;
		if (!char.IsDigit(text[lastIdx]))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ArgMustEndInInteger));
		}

		var numStart = lastIdx;
		while (numStart > 0 && (char.IsDigit(text[numStart - 1]) || text[numStart - 1] == '-'))
		{
			if (text[numStart - 1] == '-')
			{
				numStart--;
				break;
			}
			numStart--;
		}

		var prefix = text[..numStart];
		var numPart = text[numStart..];

		if (!int.TryParse(numPart, out var num))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ArgMustEndInInteger));
		}

		return ValueTask.FromResult<CallState>($"{prefix}{num + 1}");
	}

	[SharpFunction(Name = "lmath", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["op", "list", "delim"])]
	public static async ValueTask<CallState> LMath(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var args = parser.CurrentState.ArgumentsOrdered;

		var operation = MModule.plainText(args["0"].Message).ToLower();
		var delimiter = args.Count == 3 ? args["2"].Message : MModule.single(" ");
		var list = MModule.splitList(delimiter, args["1"].Message).ToList();

		var values = new List<decimal>();
		foreach (var item in list)
		{
			if (!decimal.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(item)), out var parsedValue))
			{
				return ErrorMessages.Returns.Numbers;
			}
			values.Add(parsedValue);
		}

		if (values.Count == 0)
		{
			return new CallState("0");
		}

		if ((operation == "div" || operation == "fdiv" || operation == "modulo" || operation == "remainder")
				&& values.Skip(1).Any(v => v == 0))
		{
			return ErrorMessages.Returns.DivideByZero;
		}

		string result = operation switch
		{
			"add" => values.Sum().ToString(CultureInfo.InvariantCulture),
			"sub" => values.Aggregate((acc, val) => acc - val).ToString(CultureInfo.InvariantCulture),
			"mul" => values.Aggregate((acc, val) => acc * val).ToString(CultureInfo.InvariantCulture),
			"div" => values.Aggregate((acc, val) => acc / val).ToString(CultureInfo.InvariantCulture),
			"fdiv" => values.Aggregate((acc, val) => acc / val).ToString(CultureInfo.InvariantCulture),
			"modulo" => values.Aggregate((acc, val) => ((acc % val) + val) % val).ToString(CultureInfo.InvariantCulture),
			"remainder" => values.Aggregate((acc, val) => acc % val).ToString(CultureInfo.InvariantCulture),

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

			"mean" => values.Average().ToString(CultureInfo.InvariantCulture),
			"median" => CalculateMedian(values).ToString(CultureInfo.InvariantCulture),
			"stddev" => CalculateStdDev(values).ToString(CultureInfo.InvariantCulture),

			// Distance operations (requires exactly 4 or 6 values)
			"dist2d" when values.Count == 4
				=> ((decimal)Math.Sqrt((double)((values[2] - values[0]) * (values[2] - values[0]) + (values[3] - values[1]) * (values[3] - values[1])))).ToString(CultureInfo.InvariantCulture),
			"dist2d" => ErrorMessages.Returns.BadArgumentFormat.Replace("{0}", "lmath"),
			"dist3d" when values.Count == 6
				=> ((decimal)Math.Sqrt((double)((values[3] - values[0]) * (values[3] - values[0]) + (values[4] - values[1]) * (values[4] - values[1]) + (values[5] - values[2]) * (values[5] - values[2])))).ToString(CultureInfo.InvariantCulture),
			"dist3d" => ErrorMessages.Returns.BadArgumentFormat.Replace("{0}", "lmath"),

			_ => ErrorMessages.Returns.BadArgumentFormat.Replace("{0}", "lmath")
		};

		return new CallState(result);
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
		if (values.Count <= 1)
		{
			return 0;
		}
		var mean = values.Average();
		var sumOfSquaredDifferences = values.Select(val => (val - mean) * (val - mean)).Sum();
		// PennMUSH uses sample stddev (÷(n-1)), not population stddev (÷n)
		var variance = sumOfSquaredDifferences / (values.Count - 1);
		return (decimal)Math.Sqrt((double)variance);
	}

	[SharpFunction(Name = "lnum", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["start", "end", "separator", "step"])]
	public static async ValueTask<CallState> LNum(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await Task.CompletedTask;
		var args = parser.CurrentState.ArgumentsOrdered;

		var arg0Text = MModule.plainText(args["0"].Message);

		// Single arg: lnum(count) -> 0..count-1, count must be integer-like
		if (args.Count == 1)
		{
			if (!double.TryParse(arg0Text, out var count) || double.IsNaN(count) || double.IsInfinity(count))
			{
				return new CallState(ErrorMessages.Returns.Integer);
			}
			var intCount = (int)count;
			if (intCount < 0) return new CallState(ErrorMessages.Returns.Integer);
			return new CallState(string.Join(" ", Enumerable.Range(0, intCount)));
		}

		// Multi-arg: lnum(start, end[, sep[, step]])
		if (!double.TryParse(arg0Text, out var start) || double.IsNaN(start) || double.IsInfinity(start))
		{
			return new CallState(ErrorMessages.Returns.Integer);
		}

		var arg1Text = MModule.plainText(args["1"].Message);
		if (!double.TryParse(arg1Text, out var end) || double.IsNaN(end) || double.IsInfinity(end))
		{
			return new CallState(ErrorMessages.Returns.Integer);
		}

		var delim = MModule.plainText(ArgHelpers.NoParseDefaultNoParseArgument(args, 2, " "));

		var stepText = MModule.plainText(ArgHelpers.NoParseDefaultNoParseArgument(args, 3, "1"));
		if (!double.TryParse(stepText, out var step) || double.IsNaN(step) || double.IsInfinity(step) || Math.Abs(step) < 1e-12)
		{
			return new CallState(ErrorMessages.Returns.Integer);
		}

		var useIntegers = Math.Abs(start - Math.Floor(start)) < 1e-10
			&& Math.Abs(end - Math.Floor(end)) < 1e-10
			&& Math.Abs(step - Math.Floor(step)) < 1e-10;

		var results = new List<string>();
		if (step > 0)
		{
			for (var i = start; i <= end + step * 0.0001; i += step)
			{
				if (i > end + step * 0.5) break;
				results.Add(useIntegers ? ((long)Math.Round(i)).ToString() : FormatDouble(i));
			}
		}
		else
		{
			for (var i = start; i >= end + step * 0.0001; i += step)
			{
				if (i < end + step * 0.5) break;
				results.Add(useIntegers ? ((long)Math.Round(i)).ToString() : FormatDouble(i));
			}
		}

		return new CallState(string.Join(delim, results));
	}

	private static string FormatDouble(double value)
	{
		if (Math.Abs(value - Math.Floor(value)) < 1e-10)
			return ((long)value).ToString();
		return value.ToString($"G{Library.Definitions.Configurable.FloatPrecision}");
	}

	[SharpFunction(Name = "mean", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number..."])]
	public static ValueTask<CallState> Mean(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var values = new List<double>();

		foreach (var arg in args)
		{
			if (!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(arg.Value.Message)), out var value))
			{
				return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
			}
			values.Add(value);
		}

		var mean = values.Average();
		return ValueTask.FromResult<CallState>(mean);
	}

	[SharpFunction(Name = "median", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number..."])]
	public static ValueTask<CallState> Median(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var values = new List<double>();

		foreach (var arg in args)
		{
			if (!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(arg.Value.Message)), out var value))
			{
				return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
			}
			values.Add(value);
		}

		if (values.Count == 0)
		{
			return ValueTask.FromResult<CallState>(0);
		}

		var sorted = values.OrderBy(x => x).ToList();
		var count = sorted.Count;

		if (count % 2 == 1)
		{
			return ValueTask.FromResult<CallState>(sorted[count / 2]);
		}
		else
		{
			var median = (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0;
			return ValueTask.FromResult<CallState>(median);
		}
	}

	[SharpFunction(Name = "modulo", MinArgs = 2, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["dividend", "divisor..."])]
	public static ValueTask<CallState> Modulo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var values = new List<int>();

		foreach (var arg in args)
		{
			if (!int.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(arg.Value.Message)), out var value))
			{
				return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Integers);
			}

			if (values.Count > 0 && value == 0)
			{
				return ValueTask.FromResult(new CallState(ErrorMessages.Returns.DivideByZero));
			}

			values.Add(value);
		}

		// PennMUSH modulo() uses floor-mod: result = ((a % b) + b) % b
		var result = values.Aggregate((acc, val) => ((acc % val) + val) % val);
		return ValueTask.FromResult<CallState>(result.ToString(CultureInfo.InvariantCulture));
	}

	[SharpFunction(Name = "remainder", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["dividend", "divisor..."])]
	public static ValueTask<CallState> Remainder(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var values = new List<double>();

		foreach (var arg in args)
		{
			if (!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(arg.Value.Message)), out var value))
			{
				return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
			}

			if (values.Count > 0 && value == 0)
			{
				return ValueTask.FromResult(new CallState(ErrorMessages.Returns.DivideByZero));
			}

			values.Add(value);
		}

		var result = values.Aggregate((acc, val) => acc % val);
		return ValueTask.FromResult<CallState>(result);
	}

	[SharpFunction(Name = "root", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number", "n"])]
	public static ValueTask<CallState> Root(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;

		if (!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)), out var value) ||
				!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["1"].Message)), out var root))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
		}

		// Handle negative base: odd integer roots of negatives are valid (e.g. root(-27,3) = -3)
		if (value < 0)
		{
			if (root == Math.Floor(root) && (int)root % 2 != 0)
			{
				var result = -Math.Pow(-value, 1.0 / root);
				return ValueTask.FromResult<CallState>(result);
			}

			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ImaginaryNumber));
		}

		var computed = Math.Pow(value, 1.0 / root);
		if (double.IsNaN(computed))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ImaginaryNumber));
		}

		return ValueTask.FromResult<CallState>(computed);
	}

	[SharpFunction(Name = "sign", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["number"])]
	public static ValueTask<CallState> Sign(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.EvaluateDecimalToInteger(parser.CurrentState.ArgumentsOrdered, Math.Sign);

	[SharpFunction(Name = "trunc", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["number"])]
	public static ValueTask<CallState> Truncate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.EvaluateDecimalToInteger(parser.CurrentState.ArgumentsOrdered, x => (int)Math.Truncate(x));

	[SharpFunction(Name = "acos", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["cosine", "angle-type"])]
	public static async ValueTask<CallState> ACos(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var angleArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var angleType = parser.CurrentState.Arguments.TryGetValue("1", out var value)
			? value.Message!.ToPlainText()
			: null;

		if (!double.TryParse(angleArg, out var angle))
		{
			return ErrorMessages.Returns.Number;
		}

		return InverseAngleTypeMath(angleType, angle, Math.Acos);
	}

	[SharpFunction(Name = "asin", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["sine", "angle-type"])]
	public static async ValueTask<CallState> ASin(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var angleArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var angleType = parser.CurrentState.Arguments.TryGetValue("1", out var value)
			? value.Message!.ToPlainText()
			: null;

		if (!double.TryParse(angleArg, out var angle))
		{
			return ErrorMessages.Returns.Number;
		}

		return InverseAngleTypeMath(angleType, angle, Math.Asin);
	}

	[SharpFunction(Name = "atan", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["tangent", "angle-type"])]
	public static async ValueTask<CallState> ATan(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var angleArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var angleType = parser.CurrentState.Arguments.TryGetValue("1", out var value)
			? value.Message!.ToPlainText()
			: null;

		if (!double.TryParse(angleArg, out var angle))
		{
			return ErrorMessages.Returns.Number;
		}

		return InverseAngleTypeMath(angleType, angle, Math.Atan);
	}

	[SharpFunction(Name = "atan2", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number1", "number2", "angle-type"])]
	public static async ValueTask<CallState> ATan2(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var args = parser.CurrentState.ArgumentsOrdered;

		if (!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)), out var y) ||
				!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["1"].Message)), out var x))
		{
			return ErrorMessages.Returns.Numbers;
		}

		var angleType = args.Count == 3 ? MModule.plainText(args["2"].Message) : null;

		return AngleTypeMath(angleType, Math.Atan2(y, x), angle => angle);
	}

	[SharpFunction(Name = "ceil", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["number"])]
	public static ValueTask<CallState> Ceil(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ArgHelpers.EvaluateDouble(parser.CurrentState.ArgumentsOrdered, Math.Ceiling);

	[SharpFunction(Name = "cos", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["angle", "angle-type"])]
	public static async ValueTask<CallState> Cos(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var angleArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var angleType = parser.CurrentState.Arguments.TryGetValue("1", out var value)
			? value.Message!.ToPlainText()
			: null;

		if (!double.TryParse(angleArg, out var angle))
		{
			return ErrorMessages.Returns.Number;
		}

		return AngleTypeMath(angleType, angle, Math.Cos);
	}

	[SharpFunction(Name = "ctu", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["angle", "from", "to"])]
	public static ValueTask<CallState> CTU(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;

		if (!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)), out var angle))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Number);
		}

		var from = MModule.plainText(args["1"].Message).ToLower();
		var to = MModule.plainText(args["2"].Message).ToLower();

		double radians = from switch
		{
			"r" => angle,
			"d" => angle * (Math.PI / 180.0),
			"g" => angle * (Math.PI / 200.0),
			_ => angle
		};

		double result = to switch
		{
			"r" => radians,
			"d" => radians * (180.0 / Math.PI),
			"g" => radians * (200.0 / Math.PI),
			_ => radians
		};

		return ValueTask.FromResult<CallState>(result);
	}

	[SharpFunction(Name = "e", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["number"])]
	public static ValueTask<CallState> E(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arguments = parser.CurrentState.Arguments;
		var arg1 = arguments["1"]?.Message?.ToString();

		return ValueTask.FromResult<CallState>(new(double.TryParse(arg1 ?? "1", out var dec)
			? Math.Exp(dec).ToString()
			: ErrorMessages.Returns.Number));
	}

	[SharpFunction(Name = "fmod", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number", "divisor"])]
	public static ValueTask<CallState> FMod(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;

		if (!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)), out var arg0) ||
				!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["1"].Message)), out var arg1))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
		}

		if (arg1 == 0)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.DivideByZero));
		}

		return ValueTask.FromResult<CallState>(arg0 % arg1);
	}

	[SharpFunction(Name = "floor", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["number"])]
	public static ValueTask<CallState> Floor(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ArgHelpers.EvaluateDouble(parser.CurrentState.ArgumentsOrdered, Math.Floor);

	[SharpFunction(Name = "log", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number", "base"])]
	public static ValueTask<CallState> Log(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;

		if (!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)), out var value))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
		}

		if (value < 0)
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.OutOfRange);
		}

		if (args.Count == 1)
		{
			var result = Math.Log10(value);
			if (double.IsNegativeInfinity(result))
				return ValueTask.FromResult(new CallState("-inf"));
			return ValueTask.FromResult<CallState>(result);
		}

		var baseStr = MModule.plainText(args["1"].Message).Trim();
		double baseNum;
		if (baseStr.Equals("e", StringComparison.OrdinalIgnoreCase))
		{
			baseNum = Math.E;
		}
		else if (!double.TryParse(ArgHelpers.EmptyStringToZero(baseStr), out baseNum))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
		}

		var logResult = Math.Log(value, baseNum);
		if (double.IsNegativeInfinity(logResult))
			return ValueTask.FromResult(new CallState("-inf"));
		return ValueTask.FromResult<CallState>(logResult);
	}

	[SharpFunction(Name = "ln", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["number"])]
	public static ValueTask<CallState> Ln(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var text = ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message));
		if (!double.TryParse(text, out var value))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Number);
		}
		if (value < 0)
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.OutOfRange);
		}
		var result = Math.Log(value);
		if (double.IsNegativeInfinity(result))
			return ValueTask.FromResult(new CallState("-inf"));
		return ValueTask.FromResult<CallState>(result);
	}

	[SharpFunction(Name = "pi", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public static ValueTask<CallState> PI(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ValueTask.FromResult<CallState>(Math.PI);

	[SharpFunction(Name = "power", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number", "exponent"])]
	public static ValueTask<CallState> Power(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;

		if (!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)), out var baseNum) ||
				!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["1"].Message)), out var exponent))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
		}

		return ValueTask.FromResult<CallState>(Math.Pow(baseNum, exponent));
	}

	[SharpFunction(Name = "round", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number", "places", "pad"])]
	public static ValueTask<CallState> Round(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;

		if (!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["0"].Message)), out var value) ||
				!int.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["1"].Message)), out var decimals))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
		}

		var rounded = Math.Round(value, decimals);

		if (args.Count == 3)
		{
			if (!int.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(args["2"].Message)), out var padZeros))
			{
				return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
			}

			if (padZeros != 0)
			{
				return ValueTask.FromResult<CallState>(rounded.ToString($"F{decimals}"));
			}
		}

		return ValueTask.FromResult<CallState>(rounded);
	}

	[SharpFunction(Name = "sin", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["angle", "angle-type"])]
	public static async ValueTask<CallState> Sin(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var angleArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var angleType = parser.CurrentState.Arguments.TryGetValue("1", out var value)
			? value.Message!.ToPlainText()
			: null;

		if (!double.TryParse(angleArg, out var angle))
		{
			return ErrorMessages.Returns.Number;
		}

		return AngleTypeMath(angleType, angle, Math.Sin);
	}

	[SharpFunction(Name = "sqrt", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly, ParameterNames = ["number"])]
	public static ValueTask<CallState> Sqrt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var text = ArgHelpers.EmptyStringToZero(MModule.plainText(parser.CurrentState.ArgumentsOrdered["0"].Message));
		if (!double.TryParse(text, out var value))
		{
			return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Number);
		}

		if (value < 0)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ImaginaryNumber));
		}

		return ValueTask.FromResult<CallState>(Math.Sqrt(value));
	}

	[SharpFunction(Name = "stddev", MinArgs = 1, MaxArgs = int.MaxValue,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["number..."])]
	public static ValueTask<CallState> StdDev(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var values = new List<double>();

		foreach (var arg in args)
		{
			if (!double.TryParse(ArgHelpers.EmptyStringToZero(MModule.plainText(arg.Value.Message)), out var value))
			{
				return ValueTask.FromResult<CallState>(ErrorMessages.Returns.Numbers);
			}
			values.Add(value);
		}

		if (values.Count <= 1)
		{
			return ValueTask.FromResult<CallState>(0);
		}

		var mean = values.Average();
		var sumOfSquaredDifferences = values.Select(val => Math.Pow(val - mean, 2)).Sum();
		// PennMUSH uses sample stddev (÷(n-1)), not population stddev (÷n)
		var variance = sumOfSquaredDifferences / (values.Count - 1);
		var stdDev = Math.Sqrt(variance);

		return ValueTask.FromResult<CallState>(stdDev);
	}

	[SharpFunction(Name = "tan", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["angle", "angle-type"])]
	public static async ValueTask<CallState> Tan(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var angleArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var angleType = parser.CurrentState.Arguments.TryGetValue("1", out var value)
			? value.Message!.ToPlainText()
			: null;

		if (!double.TryParse(angleArg, out var angle))
		{
			return ErrorMessages.Returns.Number;
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
		var list1 = MModule.splitList(delimiter, parser.CurrentState.Arguments["0"].Message)
			.Select(x => (decimal.TryParse(MModule.plainText(x), out var result), result)).ToArray();
		var list2 = MModule.splitList(delimiter, parser.CurrentState.Arguments["1"].Message)
			.Select(x => (decimal.TryParse(MModule.plainText(x), out var result), result)).ToArray();

		if (list1.Any(x => !x.Item1) || list2.Any(x => !x.Item1))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Numbers));
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
		var list1 = MModule.splitList(delimiter, parser.CurrentState.Arguments["0"].Message)
			.Select(x => (decimal.TryParse(MModule.plainText(x), out var result), result)).ToArray();
		var list2 = MModule.splitList(delimiter, parser.CurrentState.Arguments["1"].Message)
			.Select(x => (decimal.TryParse(MModule.plainText(x), out var result), result)).ToArray();

		if (list1.Any(x => !x.Item1) || list2.Any(x => !x.Item1))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Numbers));
		}

		var vector1 = new Vector<decimal>(list1.Select(x => x.result).ToArray().AsSpan());
		var vector2 = new Vector<decimal>(list2.Select(x => x.result).ToArray().AsSpan());
		var vectorResult = func(vector1, vector2);

		var output = vectorResult.ToString(CultureInfo.InvariantCulture);

		return ValueTask.FromResult<CallState>(output);
	}

	[SharpFunction(Name = "vadd", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["vector1", "vector2", "delimiter", "sep"])]
	public static ValueTask<CallState> VAdd(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> VectorOperation(parser, Vector.Add);

	[SharpFunction(Name = "vcross", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["vector1", "vector2", "delimiter", "sep"])]
	public static ValueTask<CallState> vcross(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delimiter = args.TryGetValue("2", out var tmpDelimiter)
			? tmpDelimiter.Message
			: MModule.single(" ");
		var sep = args.TryGetValue("3", out var tmpSep) ? tmpSep.Message : delimiter;

		var list1 = MModule.splitList(delimiter, args["0"].Message)
			.Select(x => (decimal.TryParse(MModule.plainText(x), out var result), result)).ToArray();
		var list2 = MModule.splitList(delimiter, args["1"].Message)
			.Select(x => (decimal.TryParse(MModule.plainText(x), out var result), result)).ToArray();

		if (list1.Any(x => !x.Item1) || list2.Any(x => !x.Item1))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Numbers));
		}

		if (list1.Length != 3 || list2.Length != 3)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.VectorsMustBe3D));
		}

		var x = list1[1].result * list2[2].result - list2[1].result * list1[2].result;
		var y = list1[2].result * list2[0].result - list2[2].result * list1[0].result;
		var z = list1[0].result * list2[1].result - list2[0].result * list1[1].result;

		var output = new[] { x, y, z }.Select(v => MModule.single(v.ToString(CultureInfo.InvariantCulture)));
		return ValueTask.FromResult(new CallState(MModule.multipleWithDelimiter(sep, output)));
	}

	[SharpFunction(Name = "vsub", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["vector1", "vector2", "delimiter", "sep"])]
	public static ValueTask<CallState> vsub(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> VectorOperation(parser, Vector.Subtract);

	[SharpFunction(Name = "vmax", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["vector1", "vector2", "delimiter", "sep"])]
	public static ValueTask<CallState> vmax(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> VectorOperation(parser, Vector.Max);

	[SharpFunction(Name = "vmin", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["vector1", "vector2", "delimiter", "sep"])]
	public static ValueTask<CallState> vmin(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> VectorOperation(parser, Vector.Min);

	[SharpFunction(Name = "vmul", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["vector1", "vector2", "delimiter", "sep"])]
	public static ValueTask<CallState> vmul(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> VectorOperation(parser, Vector.Multiply);

	[SharpFunction(Name = "vdot", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["vector1", "vector2", "delimiter", "sep"])]
	public static ValueTask<CallState> vdot(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> VectorOperationToScalar(parser, Vector.Dot);

	[SharpFunction(Name = "vmag", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["vector", "delimiter"])]
	public static ValueTask<CallState> vmag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delimiter = args.TryGetValue("1", out var tmpDelimiter)
			? tmpDelimiter.Message
			: MModule.single(" ");

		var list = MModule.splitList(delimiter, args["0"].Message)
			.Select(x => (decimal.TryParse(MModule.plainText(x), out var result), result)).ToArray();

		if (list.Any(x => !x.Item1))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Numbers));
		}

		var sumOfSquares = list.Sum(x => (double)(x.result * x.result));
		var magnitude = Math.Sqrt(sumOfSquares);

		return ValueTask.FromResult<CallState>(magnitude);
	}

	[SharpFunction(Name = "vunit", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["vector", "delimiter"])]
	public static ValueTask<CallState> vunit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var delimiter = args.TryGetValue("1", out var tmpDelimiter)
			? tmpDelimiter.Message
			: MModule.single(" ");

		var list = MModule.splitList(delimiter, args["0"].Message)
			.Select(x => (decimal.TryParse(MModule.plainText(x), out var result), result)).ToArray();

		if (list.Any(x => !x.Item1))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Numbers));
		}

		// Math.Sqrt requires double; the cast from decimal to double introduces minimal precision
		// loss acceptable for vector normalization, consistent with PennMUSH float behavior.
		var magnitude = (decimal)Math.Sqrt((double)list.Sum(x => x.result * x.result));
		if (magnitude == 0)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.DivisionByZero));
		}

		var output = list.Select(x => MModule.single((x.result / magnitude).ToString(CultureInfo.InvariantCulture)));
		return ValueTask.FromResult(new CallState(MModule.multipleWithDelimiter(delimiter, output)));
	}

	[SharpFunction(Name = "vdim", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["vector", "delimiter"])]
	public static ValueTask<CallState> vdim(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(new CallState(
			MModule.splitList(
				ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 1, MModule.single(" ")),
				parser.CurrentState.Arguments["0"].Message).Length.ToString()
			));

	private static CallState AngleTypeMath(string? angleType, double angle, Func<double, double> func)
	{
		var radianAngle = angleType switch
		{
			null => angle,
			"g" => angle * (double.Pi / 200),
			"d" => angle * (double.Pi / 180),
			_ => angle
		};

		var result = func(radianAngle);

		// Check for infinity or NaN (e.g. tan(90 degrees))
		// Also check for extremely large values from floating point near-singularities
		if (double.IsInfinity(result) || double.IsNaN(result) || Math.Abs(result) > 1e15)
		{
			return new CallState(ErrorMessages.Returns.OutOfRange);
		}

		// Round very small numbers (floating point errors) to 0
		// This handles cases like cos(90 degrees) which should be 0 but gives tiny values
		if (Math.Abs(result) < 1e-6)
		{
			return new CallState("0");
		}

		// Round very close to 1 or -1 to exactly 1 or -1
		if (Math.Abs(result - 1.0) < 1e-10)
		{
			return new CallState("1");
		}
		if (Math.Abs(result + 1.0) < 1e-10)
		{
			return new CallState("-1");
		}

		return new CallState(result);
	}

	/// <summary>
	/// For inverse trig functions (acos, asin, atan), the input is a ratio (not an angle),
	/// and the output is an angle in radians that should be converted to the requested angle type.
	/// </summary>
	private static CallState InverseAngleTypeMath(string? angleType, double ratio, Func<double, double> func)
	{
		// Apply the inverse trig function (always operates on the ratio directly)
		var resultRadians = func(ratio);

		// Convert the output from radians to the requested angle type
		var result = angleType switch
		{
			"d" => resultRadians * (180.0 / double.Pi),
			"g" => resultRadians * (200.0 / double.Pi),
			_ => resultRadians // null or "r" = radians (default)
		};

		// Round very small numbers (floating point errors) to 0
		if (Math.Abs(result) < 1e-6)
		{
			return new CallState("0");
		}

		// Round to nearby integers if very close
		var rounded = Math.Round(result);
		if (Math.Abs(result - rounded) < 1e-10)
		{
			return new CallState(((int)rounded).ToString(CultureInfo.InvariantCulture));
		}

		// Format without scientific notation
		var formatted = result.ToString(CultureInfo.InvariantCulture);

		if (formatted.Contains('E') || formatted.Contains('e'))
		{
			formatted = result.ToString("F15", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
		}

		return new CallState(formatted);
	}

	[SharpFunction(Name = "RNUM", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular,
		ParameterNames = ["container", "object"])]
	public static async ValueTask<CallState> RNum(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// RNUM is deprecated - use locate() instead
		// This implements basic functionality for backwards compatibility
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var containerArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var objectArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, containerArg, All,
			async container =>
			{
				if (!await PermissionService!.CanExamine(executor, container))
				{
					return new CallState("#-1");
				}

				if (!container.IsContainer)
				{
					return new CallState("#-1");
				}

				var matches = new List<AnySharpContent>();
				await foreach (var item in container.AsContainer.Content(Mediator!))
				{
					var name = item.Object().Name;
					if (name.Equals(objectArg, StringComparison.OrdinalIgnoreCase) ||
						name.StartsWith(objectArg, StringComparison.OrdinalIgnoreCase))
					{
						matches.Add(item);
					}
				}

				if (matches.Count == 0)
				{
					return new CallState("#-1");
				}
				else if (matches.Count == 1)
				{
					return new CallState($"#{matches[0].Object().DBRef.Number}");
				}
				else
				{
					return new CallState("#-2"); // Multiple matches
				}
			});
	}

	/// <summary>
	/// Continued fraction approximation — finds the best rational approximation
	/// with denominator not exceeding maxDenom. Matches PennMUSH's algorithm.
	/// </summary>
	private static (long Numerator, long Denominator) ContinuedFractionApprox(double value, long maxDenom)
	{
		long p0 = 0, q0 = 1, p1 = 1, q1 = 0;
		var x = value;

		while (true)
		{
			var a = (long)Math.Floor(x);
			var p2 = a * p1 + p0;
			var q2 = a * q1 + q0;

			if (q2 > maxDenom)
				break;

			p0 = p1; q0 = q1;
			p1 = p2; q1 = q2;

			var remainder = x - a;
			if (Math.Abs(remainder) < 1e-10)
				break;

			x = 1.0 / remainder;
		}

		return (p1, q1);
	}
}