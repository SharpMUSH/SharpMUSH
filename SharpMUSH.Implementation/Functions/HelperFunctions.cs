using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Implementation.Tools;
using OneOf;
using SharpMUSH.Library.Models;
using System.Text.RegularExpressions;

namespace SharpMUSH.Implementation.Functions
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

		private static CallState AggregateDecimals(List<CallState> args, Func<decimal, decimal, decimal> aggregateFunction) =>
			new(args
				.Select(x => decimal.Parse(MModule.plainText(x.Message)))
				.Aggregate(aggregateFunction).ToString());

		private static CallState AggregateIntegers(List<CallState> args, Func<int, int, int> aggregateFunction) =>
			new(args
				.Select(x => int.Parse(MModule.plainText(x.Message)))
				.Aggregate(aggregateFunction).ToString());

		private static CallState ValidateIntegerAndEvaluate(List<CallState> args, Func<IEnumerable<int>, MString> aggregateFunction)
			 => new(aggregateFunction(args.Select(x => int.Parse(MModule.plainText(x.Message!)))).ToString());

		private static CallState AggregateDecimalToInt(List<CallState> args, Func<decimal, decimal, decimal> aggregateFunction) =>
			new(Math.Floor(args
				.Select(x => decimal.Parse(string.Join(string.Empty, MModule.plainText(x.Message))))
				.Aggregate(aggregateFunction)).ToString());

		private static CallState EvaluateDecimal(List<CallState> args, Func<decimal, decimal> func)
			=> new(func(decimal.Parse(MModule.plainText(args[0].Message))).ToString());

		private static CallState EvaluateInteger(List<CallState> args, Func<int, int> func)
			=> new(func(int.Parse(MModule.plainText(args[0].Message))).ToString());

		private static CallState ValidateDecimalAndEvaluatePairwise(this List<CallState> args, Func<(decimal, decimal), bool> func)
		{
			if (args.Count < 2)
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
