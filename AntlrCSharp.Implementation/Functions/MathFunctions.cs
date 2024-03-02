using AntlrCSharp.Implementation.Definitions;

namespace AntlrCSharp.Implementation.Functions
{
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

		private static CallState ValidateDecimalAndAggregate(CallState[] args, Func<decimal, decimal, decimal> aggregateFunction)
		{
			var doubles = args.Select(x =>
				(
					IsDouble: decimal.TryParse(string.Join("", MModule.plainText(x.Message)), out var b),
					Double: b
				)).ToList();

			return doubles.Any(x => !x.IsDouble)
					? new CallState(Message: Errors.ErrorNumbers)
					: new CallState(Message: doubles.Select(x => x.Double).Aggregate(aggregateFunction).ToString());
		}

		private static CallState ValidateIntegerAndAggregate(CallState[] args, Func<int, int, int> aggregateFunction)
		{
			var integers = args.Select(x =>
				(
					IsInteger: int.TryParse(string.Join("", MModule.plainText(x.Message)), out var b),
					Integer: b
				)).ToList();

			return integers.Any(x => !x.IsInteger)
					? new CallState(Message: Errors.ErrorNumbers)
					: new CallState(Message: integers.Select(x => x.Integer).Aggregate(aggregateFunction).ToString());
		}

		private static CallState ValidateDecimalAndAggregateToInt(CallState[] args, Func<decimal, decimal, decimal> aggregateFunction)
		{
			var doubles = args.Select(x =>
				(
					IsDouble: decimal.TryParse(string.Join("", MModule.plainText(x.Message)), out var b),
					Double: b
				)).ToList();

			return doubles.Any(x => !x.IsDouble)
					? new CallState(Message: Errors.ErrorNumbers)
					: new CallState(Message: Math.Floor(doubles.Select(x => x.Double).Aggregate(aggregateFunction)).ToString());
		}

		private static CallState ValidateDecimalAndEvaluate(CallState[] args, Func<decimal,decimal> func)
			=> decimal.TryParse(MModule.plainText(args[0].Message), out var dec)
				? new CallState(Errors.ErrorNumber)
				: new CallState(func(dec).ToString());

	}
}
