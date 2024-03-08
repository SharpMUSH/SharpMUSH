using AntlrCSharp.Implementation.Definitions;
using AntlrCSharp.Implementation.Tools;
using OneOf;
using SharpMUSH.Library.Models;
using System.Text.RegularExpressions;

namespace AntlrCSharp.Implementation.Functions
{
	public partial class Functions
	{
		private static Regex DatabaseReferenceRegex = DatabaseReference();
		private static Regex DatabaseReferenceWithAttributeRegex = DatabaseReferenceWithAttribute();

		/// <summary>
		/// Takes the pattern of '#DBREF/attribute' and splits it out if possible.
		/// </summary>
		/// <param name="dbrefAttr">#DBREF/Attribute</param>
		/// <returns>False if it could not be split. DBRef & Attribute if it could.</returns>
		public static OneOf<(DBRef db, string Attribute), bool> SplitDBRefAndAttr(string DBRefAttr)
		{
			var match = DatabaseReferenceWithAttributeRegex.Match(DBRefAttr);
			var dbref = match.Groups["DatabaseNumber"]?.Value;
			var ctime = match.Groups["CreationTimestamp"]?.Value;
			var attr = match.Groups["Attribute"]?.Value;

			if (string.IsNullOrEmpty(attr)) { return false; }

			return (new DBRef(int.Parse(dbref!), string.IsNullOrWhiteSpace(ctime) ? null : int.Parse(ctime)), attr);
		}

		public static OneOf<DBRef, bool> ParseDBRef(string DBRefAttr)
		{
			var match = DatabaseReferenceRegex.Match(DBRefAttr);
			var dbref = match.Groups["DatabaseNumber"]?.Value;
			var ctime = match.Groups["CreationTimestamp"]?.Value;

			if (string.IsNullOrEmpty(dbref)) { return false; }

			return (new DBRef(int.Parse(dbref!), string.IsNullOrWhiteSpace(ctime) ? null : int.Parse(ctime)));
		}

		// TODO: When a flag is implemented to only accept Integers or Decimals for the Math functions, a lot of these validation checks can go away from this sector.
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

		private static CallState ValidateIntegerAndEvaluate(CallState[] args, Func<int[], MString> aggregateFunction)
		{
			var integers = args.Select(x =>
				(
					IsInteger: int.TryParse(string.Join("", MModule.plainText(x.Message)), out var b),
					Integer: b
				)).ToList();

			return integers.Any(x => !x.IsInteger)
					? new CallState(Message: Errors.ErrorNumbers)
					: new CallState(Message: aggregateFunction(integers.Select(x => x.Integer).ToArray()).ToString());
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

		private static CallState ValidateIntegerAndEvaluate(CallState[] args, Func<int, int> func)
			=> int.TryParse(MModule.plainText(args[0].Message), out var integer)
				? new CallState(Errors.ErrorInteger)
				: new CallState(func(integer).ToString());

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

		/// <summary>
		/// A regular expression that takes the form of '#123:43143124' or '#543'.
		/// </summary>
		/// <returns>A regex that has a named group for the DBRef Number and Creation Milliseconds.</returns>
		[GeneratedRegex(@"#(?<DatabaseNumber>\d+)(?::(?<CreationTimestamp>\d+))?")]
		private static partial Regex DatabaseReference();


		/// <summary>
		/// A regular expression that takes the form of '#123:43143124' or '#543'.
		/// </summary>
		/// <returns>A regex that has a named group for the DBRef Number, Creation Milliseconds, and attribute (if any).</returns>
		[GeneratedRegex(@"#(?<DatabaseNumber>\d+)(?::(?<CreationTimestamp>\d+))?/(?<Attribute>[a-zA-Z1-9@_\-\.`]+)")]
		private static partial Regex DatabaseReferenceWithAttribute();
	}
}
