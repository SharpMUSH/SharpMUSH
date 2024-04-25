using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Implementation.Tools;
using OneOf;
using SharpMUSH.Library.Models;
using System.Text.RegularExpressions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	private readonly static Regex TimeFormatMatchRegex = TimeFormatMatch();
	private readonly static Regex NthRegex = Nth();
	private readonly static Regex TimeSpanFormatMatchRegex = TimeSpanFormatMatch();
	private readonly static Regex NameListPatternRegex = NameListPattern();


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
		obj.Powers!.Any(x => x.Name == power || x.Alias == power);

	public static string Locate(
		IMUSHCodeParser parser,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> looker,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> executor,
		string name,
		LocateFlags flags)
	{
		if ((flags &
			~(LocateFlags.PreferLockPass
			| LocateFlags.FailIfNotPreferred
			| LocateFlags.NoPartialMatches
			| LocateFlags.MatchLookerControlledObjects)) != 0)
		{
			flags |= (LocateFlags.All | LocateFlags.MatchAgainstLookerLocationName | LocateFlags.ExitsInsideOfLooker);
		}

		if (((flags &
			(LocateFlags.MatchObjectsInLookerLocation
			| LocateFlags.MatchObjectsInLookerLocation
			| LocateFlags.MatchObjectsInLookerInventory
			| LocateFlags.MatchHereForLookerLocation
			| LocateFlags.ExitsPreference
			| LocateFlags.ExitsInsideOfLooker)) != 0) &&
			(!Nearby(executor, looker) && !executor.IsSee_All() && !parser.PermissionService.Controls(executor, looker))
			)
		{
			return "#-1 NOT PERMITTED TO EVALUATE ON LOOKER";
		}

		throw new NotImplementedException();
	}

	public static DBRef? WhereIs(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> thing)
	{
		if (thing.IsT1) return null;
		var minusRoom = thing.MinusRoom();
		if (thing.IsT2) return OneOfExtensions.Home(minusRoom).Object()?.DBRef;
		else return OneOfExtensions.Location(minusRoom).Object()?.DBRef;
	}

	public static DBRef FriendlyWhereIs(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> thing)
	{
		if (thing.IsT1) return thing.Object().DBRef;
		var minusRoom = thing.MinusRoom();
		if (thing.IsT2) return OneOfExtensions.Home(minusRoom).Object().DBRef;
		else return OneOfExtensions.Location(minusRoom).Object().DBRef;
	}

	public static bool Nearby(
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> obj1,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> obj2)
	{
		if (obj1.IsT1 && obj2.IsT1) return false;

		var loc1 = FriendlyWhereIs(obj1);

		if (loc1 == obj2.Object().DBRef) return true;

		var loc2 = FriendlyWhereIs(obj2);

		return (loc2 == obj1.Object()!.DBRef) || (loc2 == loc1);
	}

	private static (string RemainingString, LocateFlags NewFlags, int Count) ParseEnglish(
		string oldName,
		LocateFlags oldFlags)
	{
		LocateFlags flags = oldFlags;
		LocateFlags saveFlags = flags;
		string name = oldName;
		string saveName = name;
		int count = 0;

		if ((flags & LocateFlags.MatchObjectsInLookerLocation) != 0)
		{
			if (name.StartsWith("this here ", StringComparison.OrdinalIgnoreCase))
			{
				name = name[10..];
				flags &= ~(LocateFlags.MatchObjectsInLookerInventory | LocateFlags.ExitsInTheRoomOfLooker);
			}
			else if (name.StartsWith("here ", StringComparison.OrdinalIgnoreCase) || name.StartsWith("this ", StringComparison.OrdinalIgnoreCase))
			{
				name = name[5..];
				flags &= ~(LocateFlags.MatchObjectsInLookerInventory | LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.MatchAgainstLookerLocationName);
			}
		}

		if (((flags & LocateFlags.MatchObjectsInLookerInventory) != 0) && (name.StartsWith("my ", StringComparison.OrdinalIgnoreCase) || name.StartsWith("me ", StringComparison.OrdinalIgnoreCase)))
		{
			name = name[3..];
			flags &= ~(LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.MatchAgainstLookerLocationName);
		}

		if (((flags & (LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.ExitsInsideOfLooker)) != 0) && (name.StartsWith("toward ", StringComparison.OrdinalIgnoreCase)))
		{
			name = name[7..];
			flags &= ~(LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.MatchObjectsInLookerInventory | LocateFlags.MatchAgainstLookerLocationName);
		}

		name = name.TrimStart();

		if (string.IsNullOrWhiteSpace(name))
		{
			return (saveName, saveFlags, 0);
		}

		if (!char.IsDigit(name[0]))
		{
			return (name, flags, 0);
		}

		var mName = name.Split(' ').FirstOrDefault();
		if (string.IsNullOrWhiteSpace(mName))
		{
			return (name, flags, 0);
		}

		var ordinalMatch = NthRegex.Match(mName);

		if (ordinalMatch.Success)
		{
			count = int.Parse(ordinalMatch.Groups["Number"].Value);
			var ordinal = ordinalMatch.Groups["Ordinal"].Value;

			// This is really only valid in English.
			if (count < 1
				|| Enumerable.Range(10, 14).Contains(count) && !ordinal.Equals("th", StringComparison.CurrentCultureIgnoreCase)
				|| count % 10 == 1 && !ordinal.Equals("st", StringComparison.CurrentCultureIgnoreCase)
				|| count % 10 == 2 && !ordinal.Equals("nd", StringComparison.CurrentCultureIgnoreCase)
				|| count % 10 == 3 && !ordinal.Equals("rd", StringComparison.CurrentCultureIgnoreCase)
				|| ordinal != "th")
			{
				return (name, flags, 0);
			}
		}

		return (name[mName.Length..].TrimStart(), flags, count);
	}

	public static IEnumerable<OneOf<DBRef, string>> NameList(string list)
		=> NameListPatternRegex.Matches(list).Cast<Match>().Select(x =>
			!string.IsNullOrWhiteSpace(x.Groups["DBRef"].Value)
				? OneOf<DBRef, string>.FromT0(HelperFunctions.ParseDBRef(x.Groups["DBRef"].Value).Value())
				: OneOf<DBRef, string>.FromT1(x.Groups["User"].Value));

	/// <summary>
	/// A regular expression that matches one or more names in a list format.
	/// </summary>
	/// <returns>A regex that has a named group for the match.</returns>
	[GeneratedRegex("(\"(?<User>.+?)\"|(?<DBRef>#\\d+(:\\d+)?)|(?<User>\\S+))(\\s+|$)")]
	private static partial Regex NameListPattern();

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

	/// <summary>
	/// A regular expression that checks if a string is a number followed by an ordinal indicator.
	/// </summary>
	/// <returns>A regex that has a Named Group for Number and Ordinal.</returns>
	[GeneratedRegex(@"^(?<Number>\d+)(?<Ordinal>rd|th|nd|st)$")]
	private static partial Regex Nth();
}
