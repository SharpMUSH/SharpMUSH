namespace SharpMUSH.Implementation.Common;

public static class TimeHelpers
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
		string.Join(" ",
			ExtractArray(span)
				.SkipWhile((x, y) => ignoreZero ? x.Item1 == 0 : y < 5 - accuracy)
				.Take(accuracy)
				.DefaultIfEmpty((0, "s"))
				.Select(x => $"{x.Item1.ToString().PadRight(pad, padding)}{x.Item2}"));
}