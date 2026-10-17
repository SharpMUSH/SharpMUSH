namespace SharpMUSH.Tests.Infrastructure;

public sealed record DateExpressionExample(string Expression, string ExpectedUtc);

/// <summary>One fixed-reference corpus exercised through library, MUSH function and scene helper.</summary>
public static class DateExpressionCorpus
{
	public const string Reference = "1792152000"; // 2026-10-16T12:00Z, Friday 07:00 Chicago.
	public const string Zone = "America/Chicago";
	public static IEnumerable<Func<DateExpressionExample>> Cases() => Examples.Select(example => (Func<DateExpressionExample>)(() => example));
	public static readonly DateExpressionExample[] Examples =
	[
		new("today at 19:00", "2026-10-17T00:00:00Z"),
		new("tomorrow 7pm", "2026-10-18T00:00:00Z"),
		new("  ToMoRRow   at  7 PM  ", "2026-10-18T00:00:00Z"),
		new("the day after tomorrow at noon", "2026-10-18T17:00:00Z"),
		new("Tuesday at 18:30", "2026-10-20T23:30:00Z"),
		new("Tue 6:30pm", "2026-10-20T23:30:00Z"),
		new("this Friday 8pm", "2026-10-17T01:00:00Z"),
		new("next Tuesday 18:30", "2026-10-20T23:30:00Z"),
		new("next week Tuesday 18:30", "2026-10-20T23:30:00Z"),
		new("Friday 6am", "2026-10-23T11:00:00Z"),
		new("next Friday 8pm", "2026-10-24T01:00:00Z"),
		new("in 90 minutes", "2026-10-16T13:30:00Z"),
		new("in 2 hours", "2026-10-16T14:00:00Z"),
		new("2 hours from now", "2026-10-16T14:00:00Z"),
		new("+1h30m", "2026-10-16T13:30:00Z"),
		new("2 hours ago", "2026-10-16T10:00:00Z"),
		new("in 7 days at 18:30", "2026-10-23T23:30:00Z"),
		new("+7d 18:30", "2026-10-23T23:30:00Z"),
		new("two weeks from now at 7pm", "2026-10-31T00:00:00Z"),
		new("in a fortnight at 19:00", "2026-10-31T00:00:00Z"),
		new("October 23 at 7pm", "2026-10-24T00:00:00Z"),
		new("23 Oct 2026 19:00", "2026-10-24T00:00:00Z"),
		new("2026-10-23 19:00", "2026-10-24T00:00:00Z"),
		new("2026-10-23 at 7pm", "2026-10-24T00:00:00Z"),
		new("2026-10-23T19:00:00Z", "2026-10-23T19:00:00Z"),
		new("2026-10-23T19:00:00+02:00", "2026-10-23T17:00:00Z"),
		new("1792152000.125", "2026-10-16T12:00:00.125Z"),
		new("18:30", "2026-10-16T23:30:00Z"),
		new("6:30pm", "2026-10-16T23:30:00Z"),
		new("noon", "2026-10-16T17:00:00Z"),
		new("midnight", "2026-10-16T05:00:00Z"),
		new("6am", "2026-10-16T11:00:00Z"),
		new("tomorrow 18:30 America/Chicago", "2026-10-17T23:30:00Z"),
		new("tomorrow 18:30 UTC", "2026-10-17T18:30:00Z"),
		new("tomorrow 18:30 +02:00", "2026-10-17T16:30:00Z"),
		new("a week from Tuesday at 7pm", "2026-10-28T00:00:00Z"),
		new("first Saturday of next month at noon", "2026-11-07T18:00:00Z"),
		new("last Friday of November at 18:00", "2026-11-28T00:00:00Z"),
	];
}
