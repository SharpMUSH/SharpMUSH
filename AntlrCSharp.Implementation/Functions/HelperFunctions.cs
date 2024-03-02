using AntlrCSharp.Implementation.Definitions;
using AntlrCSharp.Implementation.Tools;

namespace AntlrCSharp.Implementation.Functions
{
	public partial class Functions
	{

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

		private static CallState ValidateDecimalAndEvaluate(CallState[] args, Func<decimal, decimal> func)
			=> decimal.TryParse(MModule.plainText(args[0].Message), out var dec)
				? new CallState(Errors.ErrorNumber)
				: new CallState(func(dec).ToString());
		private static CallState ValidateDecimalAndEvaluatePairwise(this CallState[] args, Func<(decimal, decimal), bool> func)
		{
			if (args.Length < 2)
			{
				return new CallState(Message: Errors.ErrorTooFewArguments);
			}

			var doubles = args.Select(x =>
				(
					IsDouble: decimal.TryParse(string.Join("", MModule.plainText(x.Message)), out var b),
					Double: b
				)).ToList();

			return doubles.Any(x => !x.IsDouble)
					? new CallState(Message: Errors.ErrorNumbers)
					: new CallState(Message: doubles.Select(x => x.Double).Pairwise().Skip(1).SkipWhile(func).Any().ToString());
		}
	}
}
