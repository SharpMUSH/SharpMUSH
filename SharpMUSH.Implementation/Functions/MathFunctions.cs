using AntlrCSharp.Implementation.Definitions;

namespace AntlrCSharp.Implementation.Functions
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
		[PennFunction(Name = "add", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Add(Parser parser, PennFunctionAttribute _2) =>
			ValidateDecimalAndAggregate(parser.State.Peek().Arguments, (acc, sub) => acc + sub);

		[PennFunction(Name = "sub", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Sub(Parser parser, PennFunctionAttribute _2) =>
			ValidateDecimalAndAggregate(parser.State.Peek().Arguments, (acc, sub) => acc - sub);

		[PennFunction(Name = "mul", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Mul(Parser parser, PennFunctionAttribute _2) =>
			ValidateDecimalAndAggregate(parser.State.Peek().Arguments, (acc, sub) => acc * sub);

		[PennFunction(Name = "div", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.IntegersOnly)]
		public static CallState Div(Parser parser, PennFunctionAttribute _2) =>
			ValidateIntegerAndAggregate(parser.State.Peek().Arguments, (acc, sub) => acc / sub);

		[PennFunction(Name = "fdiv", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState FDiv(Parser parser, PennFunctionAttribute _2) =>
			ValidateDecimalAndAggregate(parser.State.Peek().Arguments, (acc, sub) => acc / sub);

		[PennFunction(Name = "floordiv", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState FloorDiv(Parser parser, PennFunctionAttribute _2) =>
			ValidateDecimalAndAggregateToInt(parser.State.Peek().Arguments, (acc, sub) => acc / sub);

		[PennFunction(Name = "max", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Max(Parser parser, PennFunctionAttribute _2) =>
			ValidateDecimalAndAggregate(parser.State.Peek().Arguments, Math.Max);

		[PennFunction(Name = "min", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Min(Parser parser, PennFunctionAttribute _2) =>
			ValidateDecimalAndAggregate(parser.State.Peek().Arguments, Math.Min);

		[PennFunction(Name = "abs", MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Abs(Parser parser, PennFunctionAttribute _2)
			=> ValidateDecimalAndEvaluate(parser.State.Peek().Arguments, Math.Abs);
	}
}