using AntlrCSharp.Implementation.Definitions;

namespace AntlrCSharp.Implementation.Functions
{
	public static partial class Functions
	{
		private static CallState ValidateAndEvaluate(CallState[] args, Func<decimal, decimal, decimal> aggregateFunction)
		{
			var doubles = args.Select(x =>
				(
					IsDouble: decimal.TryParse(string.Join("", MModule.plainText(x?.Message)), out var b),
					Double: b
				));

			return doubles.Any(x => !x.IsDouble)
					? new CallState(Message: Errors.ErrorNumbers)
					: new CallState(Message: doubles.Select(x => x.Double).Aggregate(aggregateFunction).ToString());
		}

		[PennFunction(Name = "add", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Add(Parser _1, PennFunctionAttribute _2, params CallState[] args) =>
			ValidateAndEvaluate(args, (acc, sub) => acc + sub);

		[PennFunction(Name = "sub", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Sub(Parser _1, PennFunctionAttribute _2, params CallState[] args) =>
			ValidateAndEvaluate(args, (acc, sub) => acc - sub);

		[PennFunction(Name = "mul", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Mul(Parser _1, PennFunctionAttribute _2, params CallState[] args) =>
			ValidateAndEvaluate(args, (acc, sub) => acc * sub);

		[PennFunction(Name = "div", Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.DecimalsOnly)]
		public static CallState Div(Parser _1, PennFunctionAttribute _2, params CallState[] args) =>
			ValidateAndEvaluate(args, (acc, sub) => acc / sub);
	}
}
