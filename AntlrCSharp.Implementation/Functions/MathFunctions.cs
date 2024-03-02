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
		public static CallState Add(Parser _1, PennFunctionAttribute _2, params CallState[] args) =>
			ValidateDecimalAndAggregate(args, (acc, sub) => acc + sub);

		[PennFunction(Name = "sub", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Sub(Parser _1, PennFunctionAttribute _2, params CallState[] args) =>
			ValidateDecimalAndAggregate(args, (acc, sub) => acc - sub);

		[PennFunction(Name = "mul", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Mul(Parser _1, PennFunctionAttribute _2, params CallState[] args) =>
			ValidateDecimalAndAggregate(args, (acc, sub) => acc * sub);

		[PennFunction(Name = "div", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Div(Parser _1, PennFunctionAttribute _2, params CallState[] args) =>
			ValidateIntegerAndAggregate(args, (acc, sub) => acc / sub);

		[PennFunction(Name = "fdiv", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState FDiv(Parser _1, PennFunctionAttribute _2, params CallState[] args) =>
			ValidateDecimalAndAggregate(args, (acc, sub) => acc / sub);

		[PennFunction(Name = "floordiv", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState FloorDiv(Parser _1, PennFunctionAttribute _2, params CallState[] args) =>
			ValidateDecimalAndAggregateToInt(args, (acc, sub) => acc / sub);

		[PennFunction(Name = "max", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Max(Parser _1, PennFunctionAttribute _2, params CallState[] args) =>
			ValidateDecimalAndAggregate(args, Math.Max);

		[PennFunction(Name = "min", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Min(Parser _1, PennFunctionAttribute _2, params CallState[] args) =>
			ValidateDecimalAndAggregate(args, Math.Min);

		[PennFunction(Name = "abs", MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Abs(Parser _1, PennFunctionAttribute _2, params CallState[] args)
			=> ValidateDecimalAndEvaluate(args, Math.Abs);
	}
}