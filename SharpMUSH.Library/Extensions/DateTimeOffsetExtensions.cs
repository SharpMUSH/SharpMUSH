namespace SharpMUSH.Library.Extensions;

public static class DateTimeOffsetExtensions
{
	public static DateTimeOffset Next(this DateTimeOffset date, DayOfWeek dow) =>
		date.AddDays(date.DayOfWeek < dow
			? dow - date.DayOfWeek
			: 7 - (int)date.DayOfWeek + (int)dow);

	public static DateTimeOffset FirstOfYear(this DateTimeOffset date, DayOfWeek day)
	{
		var startOfYear = date.AddDays(-date.DayOfYear);
		return startOfYear.DayOfWeek == day ? startOfYear : Next(startOfYear, day);
	}

	public static double DaysSinceDateToDate(this DateTimeOffset date, DateTimeOffset other)
		=> (date - other).TotalDays;
}