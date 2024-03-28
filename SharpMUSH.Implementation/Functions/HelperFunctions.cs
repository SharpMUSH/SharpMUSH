using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Implementation.Tools;
using OneOf;
using SharpMUSH.Library.Models;
using System.Text.RegularExpressions;
using OneOf.Monads;

namespace SharpMUSH.Implementation.Functions
{
	public partial class Functions
	{
		private static Regex DatabaseReferenceRegex = DatabaseReference();
		private static Regex DatabaseReferenceWithAttributeRegex = DatabaseReferenceWithAttribute();
		private static Regex TimeFormatMatchRegex = TimeFormatMatch();
		private static Regex TimeSpanFormatMatchRegex = TimeSpanFormatMatch();
		private static Regex NameListPatternRegex = NameListPattern();

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

			return (new DBRef(int.Parse(dbref!), string.IsNullOrWhiteSpace(ctime) ? null : long.Parse(ctime)), attr);
		}

		public static Option<DBRef> ParseDBRef(string DBRefAttr)
		{
			var match = DatabaseReferenceRegex.Match(DBRefAttr);
			var dbref = match.Groups["DatabaseNumber"]?.Value;
			var ctime = match.Groups["CreationTimestamp"]?.Value;

			if (string.IsNullOrEmpty(dbref)) { return new None(); }

			return (new DBRef(int.Parse(dbref!), string.IsNullOrWhiteSpace(ctime) ? null : long.Parse(ctime)));
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

		private static CallState EvaluateDouble(List<CallState> args, Func<double, double> func)
			=> new(func(double.Parse(MModule.plainText(args[0].Message))).ToString());

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

		private static (int, string)[] ExtractArray(TimeSpan span) =>
			[
				(span.Days > 6 ? span.Days / 7 : 0, "w"),
				(span.Days < 7 ? span.Days : span.Days % 7, "d"),
				(span.Hours, "h"),
				(span.Minutes, "m"),
				(span.Seconds, "s")
			];

		public static string TimeString(TimeSpan span, int pad = 0, char padding = '0', ushort accuracy = 1, bool ignoreZero = true) =>
			$"{string.Join(" ",
				ExtractArray(span)
				.SkipWhile((x, y) => ignoreZero ? x.Item1 == 0 : y < 5 - accuracy)
				.Take(accuracy)
				.DefaultIfEmpty((0, "s"))
				.Select(x => $"{x.Item1.ToString().PadRight(pad, padding)}{x.Item2}"))}";

		public static string TimeFormat(DateTimeOffset time, string format)
			=> TimeFormatMatchRegex.Replace(format, match =>
				match.Groups["Character"].Value switch
				{
					// Abbreviated weekday name 
					"a" => string.Empty,
					// Full weekday name
					"A" => string.Empty,
					// Abbreviated month name
					"b" => string.Empty,
					// Full month name  
					"B" => string.Empty,
					// Date and time 
					"c" => string.Empty,
					// Day of the month
					"d" => string.Empty,
					// Hour of the 24-hour day
					"H" => time.Hour.ToString(),
					// Hour of the 12-hour day
					"I" => time.Hour > 12 ? $"{time.Hour - 12}PM" : $"{time.Hour}AM",
					// Day of the year 
					"j" => time.DayOfYear.ToString(),
					// Month of the year
					"m" => string.Empty,
					// Minutes after the hour 
					"M" => string.Empty,
					"P" or "p" => string.Empty,
					// Seconds after the minute
					"S" => string.Empty,
					// Week of the year from 1rst Sunday
					"U" => string.Empty,
					// Day of the week. 0 = Sunday
					"w" => string.Empty,
					// Week of the year from 1rst Monday
					"W" => string.Empty,
					// Date 
					"x" => string.Empty,
					// Time
					"X" => time.DateTime.ToShortTimeString(),
					// Two-digit year
					"y" => time.Year.ToString("{0:2}"),
					// Four-digit year
					"Y" => time.Year.ToString(),
					// Time zone
					"Z" => time.Offset.ToString(),
					// $ character
					"$" => "$",
					_ => string.Empty,
				});

		public static string TimeSpanFormat(DateTimeOffset time, string format)
			=> TimeFormatMatchRegex.Replace(format, match =>
				{
					var character = match.Groups["Character"];
					var adjustment = match.Groups["Adjustment"].Success
						? match.Groups["Adjustment"].Value
						: null;
					var pad = adjustment?.Contains('x') ?? false;
					var append = adjustment?.Contains('z') ?? false;

					return character.Value switch
					{
						// The number of seconds
						"s" => string.Empty,
						// The number of seconds
						"S" => string.Empty,
						// The number of minutes
						"m" => string.Empty,
						// The number of minutes
						"M" => string.Empty,
						// The number of weeks
						"w" => string.Empty,
						// The number of weeks
						"W" => string.Empty,
						// The number of hours
						"h" => string.Empty,
						// The number of hours
						"H" => string.Empty,
						// The number of days
						"d" => string.Empty,
						// The number of days
						"D" => string.Empty,
						// The number of 365-day years 
						"y" => string.Empty,
						// The number of 365-day years
						"Y" => string.Empty,
						// $ character
						"$" => "$",
						_ => string.Empty,
					};
				});

		[Flags]
		public enum LocateFlags
		{
			NoTypePreference = 0,
			ExitsPreference,
			PreferLockPass,
			PlayersPreference,
			RoomsPreference,
			ThingsPreference,
			FailIfNotPreferred,
			UseLastIfAmbiguous,
			AbsoluteMatch,
			ExitsInTheRoomOfLooker,
			ExitsInsideOfLooker,
			MatchHereForLookerLocation,
			MatchObjectsInLookerInventory,
			MatchAgainstLookerLocationName,
			MatchMeForLooker,
			MatchObjectsInLookerLocation,
			MatchWildCardForPlayerName,
			MatchOptionalWildCardForPlayerName,
			EnglishStyleMatching,
			All,
			NoPartialMatches,
			MatchLookerControlledObjects
		}	

		public static bool HasObjectFlags(SharpObject obj, SharpObjectFlag flag)
			=> obj.Flags!.Contains(flag);

		public static bool HasObjectPowers(SharpObject obj, string power) =>
			obj.Powers!.Any( x => x.Name == power || x.Alias == power);

		public static string Locate(SharpObject looker, SharpObject executor, string name, LocateFlags flags)
		{
			if ((flags &
				~(LocateFlags.PreferLockPass
				| LocateFlags.FailIfNotPreferred
				| LocateFlags.NoPartialMatches
				| LocateFlags.MatchLookerControlledObjects)) != 0)
			{
				flags |= (LocateFlags.All | LocateFlags.MatchAgainstLookerLocationName | LocateFlags.ExitsInsideOfLooker);
			}

			if ((flags &
				(LocateFlags.MatchObjectsInLookerLocation
				| LocateFlags.MatchObjectsInLookerLocation
				| LocateFlags.MatchObjectsInLookerInventory
				| LocateFlags.MatchHereForLookerLocation
				| LocateFlags.ExitsPreference
				| LocateFlags.ExitsInsideOfLooker)) != 0)
			{
				// if (!nearby(executor, looker) && !See_All(executor) &&
				// !controls(executor, looker)) {
				// safe_str("#-1", buff, bp);
				// return;
			}

			throw new NotImplementedException();
		}

		public static IEnumerable<OneOf<DBRef, string>> NameList(string list)
			=> NameListPatternRegex.Matches(list).Cast<Match>().Select(x =>
				!string.IsNullOrWhiteSpace(x.Groups["DBRef"].Value)
					? OneOf<DBRef, string>.FromT0(ParseDBRef(x.Groups["DBRef"].Value).Value())
					: OneOf<DBRef, string>.FromT1(x.Groups["User"].Value));

		/// <summary>
		/// A regular expression that matches one or more names in a list format.
		/// </summary>
		/// <returns>A regex that has a named group for the match.</returns>
		[GeneratedRegex("(\"(?<User>.+?)\"|(?<DBRef>#\\d+(:\\d+)?)|(?<User>\\S+))(\\s+|$)")]
		private static partial Regex NameListPattern();

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

		/// <summary>
		/// A regular expression that puts in time formats, with the ability to escape $ with another $.
		/// </summary>
		/// <returns>A regex that has a match for each replacement.</returns>
		[GeneratedRegex(@"\$(?<Character>[aAbBcdHIjmMpSUwWxXyYZ\$])")]
		private static partial Regex TimeFormatMatch();

		/// <summary>
		/// A regular expression that puts in time formats, with the ability to escape $ with another $.
		/// </summary>
		/// <returns>A regex that has a match for each replacement.</returns>
		[GeneratedRegex(@"\$(?<Adjustment>z?x?|x?z?)(?<Character>[smwhdySMWHDY\$])")]
		private static partial Regex TimeSpanFormatMatch();
	}
}
