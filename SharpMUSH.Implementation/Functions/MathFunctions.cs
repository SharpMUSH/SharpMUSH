using SharpMUSH.Implementation.Definitions;

namespace SharpMUSH.Implementation.Functions
{
	/*
		acos()
		asin()
		atan()
		atan2()
		bound()
		ceil()
		cos()
		ctu()
		dist2d()
		dist3d()
		e()
		exp()
		floor()
		fmod()
		fraction()
		ln()
		lmath()
		log()
		mean()
		median()
		pi()
		power()
		root()
		round()
		sign()
		sin()
		sqrt()
		stddev()
		tan()
		trunc()
		val()
		dec()
		inc()
		mod()
		remainder()
	 */
	public static partial class Functions
	{
		[SharpFunction(Name = "add", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Add(Parser parser, SharpFunctionAttribute _2) =>
			ValidateDecimalAndAggregate(parser.State.Peek().Arguments, (acc, sub) => acc + sub);

		[SharpFunction(Name = "sub", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Sub(Parser parser, SharpFunctionAttribute _2) =>
			ValidateDecimalAndAggregate(parser.State.Peek().Arguments, (acc, sub) => acc - sub);

		[SharpFunction(Name = "mul", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Mul(Parser parser, SharpFunctionAttribute _2) =>
			ValidateDecimalAndAggregate(parser.State.Peek().Arguments, (acc, sub) => acc * sub);

		[SharpFunction(Name = "div", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.IntegersOnly)]
		public static CallState Div(Parser parser, SharpFunctionAttribute _2) =>
			ValidateIntegerAndAggregate(parser.State.Peek().Arguments, (acc, sub) => acc / sub);

		[SharpFunction(Name = "fdiv", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState FDiv(Parser parser, SharpFunctionAttribute _2) =>
			ValidateDecimalAndAggregate(parser.State.Peek().Arguments, (acc, sub) => acc / sub);

		[SharpFunction(Name = "floordiv", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState FloorDiv(Parser parser, SharpFunctionAttribute _2) =>
			ValidateDecimalAndAggregateToInt(parser.State.Peek().Arguments, (acc, sub) => acc / sub);

		[SharpFunction(Name = "max", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Max(Parser parser, SharpFunctionAttribute _2) =>
			ValidateDecimalAndAggregate(parser.State.Peek().Arguments, Math.Max);

		[SharpFunction(Name = "min", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Min(Parser parser, SharpFunctionAttribute _2) =>
			ValidateDecimalAndAggregate(parser.State.Peek().Arguments, Math.Min);

		[SharpFunction(Name = "abs", MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Abs(Parser parser, SharpFunctionAttribute _2)
			=> ValidateDecimalAndEvaluate(parser.State.Peek().Arguments, Math.Abs);
	}
}