using OneOf;
using SharpMUSH.Implementation.Tools;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	private static readonly Regex TimeFormatMatchRegex = TimeFormatMatch();
	private static readonly Regex TimeSpanFormatMatchRegex = TimeSpanFormatMatch();
	private static readonly Regex NameListPatternRegex = NameListPattern();

	public static MString NoParseDefaultNoParseArgument(ImmutableSortedDictionary<string, CallState> args, int item,
		MString defaultValue)
	{
		if (args.Count - 1 < item || item == 0 && string.IsNullOrEmpty(args[item.ToString()]?.Message?.ToString()) ||
		    args[item.ToString()].Message?.ToString() is null)
		{
			return defaultValue;
		}

		return args[item.ToString()].Message!;
	}

	public static MString NoParseDefaultNoParseArgument(ImmutableSortedDictionary<string, CallState> args, int item,
		string defaultValue)
		=> NoParseDefaultNoParseArgument(args, item, MModule.single(defaultValue));

	public static async ValueTask<MString> NoParseDefaultEvaluatedArgument(IMUSHCodeParser parser, int item,
		MString defaultValue)
	{
		var args = parser.CurrentState.Arguments;
		if (args.Count - 1 < item || MModule.getLength(args[item.ToString()].Message!) == 0)
		{
			return defaultValue;
		}

		return (await args[item.ToString()].ParsedMessage())!;
	}

	public static ValueTask<MString> NoParseDefaultEvaluatedArgument(IMUSHCodeParser parser, int item,
		string defaultValue)
		=> NoParseDefaultEvaluatedArgument(parser, item, MModule.single(defaultValue));

	public static async ValueTask<MString> EvaluatedDefaultEvaluatedArgument(IMUSHCodeParser parser, int item,
		CallState defaultValue)
	{
		var args = parser.CurrentState.Arguments;
		var parsedValue = (await args[item.ToString()].ParsedMessage())!;
		if (args.Count - 1 < item || MModule.getLength(parsedValue) == 0)
		{
			return (await defaultValue.ParsedMessage())!;
		}

		return parsedValue!;
	}

	private static ValueTask<CallState> AggregateDecimals(ImmutableSortedDictionary<string, CallState> args,
		Func<decimal, decimal, decimal> aggregateFunction)
	{
		try
		{
			return ValueTask.FromResult<CallState>(args
				.Select(x => decimal.Parse(EmptyStringToZero(MModule.plainText(x.Value.Message))))
				.Aggregate(aggregateFunction).ToString(CultureInfo.InvariantCulture));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumbers);
		}
	}

	private static ValueTask<CallState> AggregateIntegers(ImmutableSortedDictionary<string, CallState> args,
		Func<int, int, int> aggregateFunction)
	{
		try
		{
			return ValueTask.FromResult<CallState>(args
				.Select(x
					=> int.Parse(EmptyStringToZero(MModule.plainText(x.Value.Message))))
				.Aggregate(aggregateFunction).ToString(CultureInfo.InvariantCulture));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorIntegers);
		}
	}

	private static ValueTask<CallState> ValidateIntegerAndEvaluate(ImmutableSortedDictionary<string, CallState> args,
		Func<IEnumerable<int>, MString> aggregateFunction)
	{
		try
		{
			return ValueTask.FromResult<CallState>(
				aggregateFunction(args
					.Select(x
						=> int.Parse(EmptyStringToZero(MModule.plainText(x.Value.Message!))))));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorIntegers);
		}
	}

	private static ValueTask<CallState> AggregateDecimalToInt(ImmutableSortedDictionary<string, CallState> args,
		Func<decimal, decimal, decimal> aggregateFunction)
	{
		try
		{
			return ValueTask.FromResult<CallState>(Math.Floor(args
				.Select(x
					=> decimal.Parse(string.Join(string.Empty, EmptyStringToZero(MModule.plainText(x.Value.Message)))))
				.Aggregate(aggregateFunction)).ToString(CultureInfo.InvariantCulture));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumbers);
		}
	}

	private static ValueTask<CallState> EvaluateDecimal(ImmutableSortedDictionary<string, CallState> args,
		Func<decimal, decimal> func)
	{
		try
		{
			return ValueTask.FromResult<CallState>(
				func(decimal.Parse(EmptyStringToZero(MModule.plainText(args["0"].Message)))));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumber);
		}
	}

	private static ValueTask<CallState> EvaluateDecimalToInteger(ImmutableSortedDictionary<string, CallState> args,
		Func<decimal, int> func)
	{
		try
		{
			return ValueTask.FromResult<CallState>(
				func(decimal.Parse(EmptyStringToZero(MModule.plainText(args["0"].Message)))));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumber);
		}
	}

	private static ValueTask<CallState> EvaluateDouble(ImmutableSortedDictionary<string, CallState> args,
		Func<double, double> func)
	{
		try
		{
			return ValueTask.FromResult<CallState>(
				func(double.Parse(EmptyStringToZero(MModule.plainText(args["0"].Message)))));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumber);
		}
	}

	private static ValueTask<CallState> EvaluateInteger(ImmutableSortedDictionary<string, CallState> args,
		Func<int, int> func)
	{
		try
		{
			return ValueTask.FromResult<CallState>(func(int.Parse(EmptyStringToZero(MModule.plainText(args["0"].Message)))));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorInteger);
		}
	}

	public static string EmptyStringToZero(string input)
		=> string.IsNullOrEmpty(input) ? "0" : input;

	private static ValueTask<CallState> ValidateDecimalAndEvaluatePairwise(
		ImmutableSortedDictionary<string, CallState> args,
		Func<(decimal, decimal), bool> func)
	{
		if (args.Count < 2)
		{
			return ValueTask.FromResult(new CallState(Message: Errors.ErrorTooFewArguments));
		}

		var doubles = args.Select(x =>
		(
			IsDouble: decimal.TryParse(string.Join("", EmptyStringToZero(MModule.plainText(x.Value.Message))), out var b),
			Double: b
		)).ToList();

		if (doubles.Any(x => !x.IsDouble))
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumbers);
		}

		var result = doubles.Select(x => x.Double).Pairwise().All(func);

		return new ValueTask<CallState>(result ? "1" : "0");
	}

	private static (int, string)[] ExtractArray(TimeSpan span) =>
	[
		(span.Days > 6 ? span.Days / 7 : 0, "w"),
		(span.Days < 7 ? span.Days : span.Days % 7, "d"),
		(span.Hours, "h"),
		(span.Minutes, "m"),
		(span.Seconds, "s")
	];

	public static string TimeString(TimeSpan span, int pad = 0, char padding = '0', ushort accuracy = 1,
		bool ignoreZero = true) =>
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
				"a" => time.ToString("ddd"),
				// Full weekday name
				"A" => time.ToString("dddd"),
				// Abbreviated month name
				"b" => time.ToString("MMM"),
				// Full month name  
				"B" => time.ToString("MMMM"),
				// Date and time 
				"c" => time.ToString("g"),
				// Day of the month
				"d" => time.ToString("dd"),
				// Hour of the 24-hour day
				"H" => time.ToString("HH"),
				// Hour of the 12-hour day
				"I" => time.ToString("hh t"),
				// Day of the year 
				"j" => time.DayOfYear.ToString(),
				// Month of the year
				"m" => time.ToString("M"),
				// Minutes after the hour 
				"M" => time.ToString("m"),
				"P" or "p" => string.Empty,
				// Seconds after the minute
				"S" => time.ToString("s"),
				// Week of the year from 1rst Sunday
				"U" => $"{(time - time.FirstOfYear(DayOfWeek.Sunday)).Days / 7}",
				// Day of the week. 0 = Sunday
				"w" => time.DayOfWeek.ToString(),
				// Week of the year from 1rst Monday
				"W" => $"{(time - time.FirstOfYear(DayOfWeek.Monday)).Days / 7}",
				// Date 
				"x" => time.ToString("d"),
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

	public static string TimeSpanFormat(TimeSpan time, string format)
		=> TimeFormatMatchRegex.Replace(format, match =>
		{
			var character = match.Groups["Character"];
			var adjustment = match.Groups["Adjustment"].Success
				? match.Groups["Adjustment"].Value
				: null;
			// var pad = adjustment?.Contains('x') ?? false;
			// var append = adjustment?.Contains('z') ?? false;

			return character.Value switch
			{
				// The number of seconds
				"s" or "S" => time.Seconds.ToString(),
				// The number of minutes
				"m" or "M" => time.Minutes.ToString(),
				// The number of weeks
				"w" or "W" => (time.Days / 7).ToString(),
				// The number of hours
				"h" or "H" => time.Hours.ToString(),
				// The number of days
				"d" or "D" => time.Days.ToString(),
				// The number of 365-day years 
				"y" or "Y" => (time.Days / 365).ToString(),
				// $ character
				"$" => "$",
				_ => string.Empty,
			};
		});

	public static async ValueTask<bool> HasObjectFlags(SharpObject obj, SharpObjectFlag flag)
		=> (await obj.Flags.WithCancellation(CancellationToken.None))
			.Contains(flag);

	public static async ValueTask<bool> HasObjectPowers(SharpObject obj, string power) =>
		(await obj.Powers.WithCancellation(CancellationToken.None))
		.Any(x => x.Name == power || x.Alias == power);

	public static IEnumerable<OneOf<DBRef, string>> NameList(string list)
		=> NameListPatternRegex.Matches(list).Select(x =>
			!string.IsNullOrWhiteSpace(x.Groups["DBRef"].Value)
				? OneOf<DBRef, string>.FromT0(HelperFunctions.ParseDBRef(x.Groups["DBRef"].Value).AsValue())
				: OneOf<DBRef, string>.FromT1(x.Groups["User"].Value));

	public static async ValueTask<IEnumerable<SharpPlayer?>> PopulatedNameList(IMUSHCodeParser parser, string list)
		=> await Task.WhenAll(NameList(list).Select(x => x.Match(
			async dbref => (await Mediator!.Send(new GetObjectNodeQuery(dbref))).TryPickT0(out var player, out var _)
				? player
				: null,
			async name => (await Mediator!.Send(new GetPlayerQuery(name))).FirstOrDefault())));

	public static async ValueTask<CallState> ForHandleOrPlayer(IMUSHCodeParser parser, CallState value, Func<long, IConnectionService.ConnectionData, ValueTask<CallState>> handleFunc, Func<SharpPlayer,IConnectionService.ConnectionData,ValueTask<CallState>> playerFunc)
	{
			var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
			var valueText = MModule.plainText(value.Message);
	
			var isHandle = long.TryParse(valueText, out var handle);
			
			if (isHandle)
			{
				var handleData = ConnectionService!.Get(handle);
				if (handleData == null) return new CallState("#-1 That handle is not connected.");
				
				return await handleFunc(handle, handleData);
			}
	
			var maybeFound = await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, valueText);
	
			if (maybeFound.IsError)
			{
				return maybeFound.AsError;
			}
	
			var found = maybeFound.AsSharpObject.AsPlayer;
			var foundData = ConnectionService!.Get(found.Object.DBRef).FirstOrDefault();
			
			if(foundData == null) return new CallState("#-1 That player is not connected.");
			
			return await playerFunc(found, foundData);
	}
	
	/// <summary>
	/// A regular expression that matches one or more names in a list format.
	/// </summary>
	/// <returns>A regex that has a named group for the match.</returns>
	[GeneratedRegex("(\"(?<User>.+?)\"|(?<DBRef>#\\d+(:\\d+)?)|(?<User>\\S+))(\\s?|$)")]
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
}