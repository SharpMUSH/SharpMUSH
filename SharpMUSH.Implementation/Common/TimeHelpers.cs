using System.Text.RegularExpressions;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Implementation.Common;

public partial class TimeHelpers
{
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
		=> TimeFormatMatch().Replace(format, match =>
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
		=> TimeFormatMatch().Replace(format, match =>
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