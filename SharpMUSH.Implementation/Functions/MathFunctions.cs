using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions
{
	public static partial class Functions
	{
		[SharpFunction(Name = "add", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Add(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
			AggregateDecimals(parser.CurrentState.Arguments, (acc, sub) => acc + sub);

		[SharpFunction(Name = "sub", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Sub(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
			AggregateDecimals(parser.CurrentState.Arguments, (acc, sub) => acc - sub);

		[SharpFunction(Name = "mul", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Mul(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
			AggregateDecimals(parser.CurrentState.Arguments, (acc, sub) => acc * sub);

		[SharpFunction(Name = "div", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.IntegersOnly)]
		public static CallState Div(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
			AggregateIntegers(parser.CurrentState.Arguments, (acc, sub) => acc / sub);

		[SharpFunction(Name = "fdiv", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState FDiv(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
			AggregateDecimals(parser.CurrentState.Arguments, (acc, sub) => acc / sub);

		[SharpFunction(Name = "floordiv", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState FloorDiv(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
			AggregateDecimalToInt(parser.CurrentState.Arguments, (acc, sub) => acc / sub);

		[SharpFunction(Name = "max", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Max(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
			AggregateDecimals(parser.CurrentState.Arguments, Math.Max);

		[SharpFunction(Name = "min", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Min(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
			AggregateDecimals(parser.CurrentState.Arguments, Math.Min);

		[SharpFunction(Name = "abs", MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Abs(IMUSHCodeParser parser, SharpFunctionAttribute _2)
			=> EvaluateDecimal(parser.CurrentState.Arguments, Math.Abs);

		[SharpFunction(Name = "BOUND", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Bound(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "DEC", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Dec(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "DECODE64", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState Decode64(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "DECOMPOSE", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState Decompose(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "DECRYPT", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState Decrypt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "DIST2D", MinArgs = 4, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Distance2d(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "DIST3D", MinArgs = 6, MaxArgs = 6, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Distance3d(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ENCODE64", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
		public static CallState Encode64(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ENCRYPT", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular)]
		public static CallState Encrypt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "FRACTION", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Fraction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "INC", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Inc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LMATH", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState LMath(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LNUM", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState LNum(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "MEAN", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Mean(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "MEDIAN", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Median(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "MODULO", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Modulo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "PIDINFO", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState PIDInfo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "POWERS", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Powers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "REMAINDER", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular)]
		public static CallState Remainder(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ROOT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Root(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SIGN", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Sign(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TRUNC", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Truncate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ACOS", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState ACos(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ASIN", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState ASin(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ATAN", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState ATan(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ATAN2", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState ATan2(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "CEIL", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Ceil(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "COS", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Cos(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "CTU", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState CTU(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "E", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState E(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			var arguments = parser.CurrentState.Arguments;
			var arg1 = arguments[1]?.Message?.ToString();

			return new(double.TryParse(arg1 ?? "1", out var dec)
				? Math.Exp(dec).ToString()
				: "#-1 ARGUMENT MUST BE NUMBER");
		}

		[SharpFunction(Name = "FMOD", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState FMod(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "FLOOR", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Floor(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
			EvaluateDouble(parser.CurrentState.Arguments, Math.Floor);

		[SharpFunction(Name = "LOG", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Log(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "LN", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Ln(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
			EvaluateDouble(parser.CurrentState.Arguments, Math.Log);

		[SharpFunction(Name = "PI", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
		public static CallState PI(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
			new(Math.PI.ToString());

		[SharpFunction(Name = "POWER", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Power(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "ROUND", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Round(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SIN", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Sin(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "SQRT", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Sqrt(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
			EvaluateDouble(parser.CurrentState.Arguments, Math.Sqrt);

		[SharpFunction(Name = "STDDEV", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState StdDev(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}

		[SharpFunction(Name = "TAN", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
		public static CallState Tan(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		{
			throw new NotImplementedException();
		}
	}
}