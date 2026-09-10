using SharpMUSH.Library.Services.RecurringJobs;

namespace SharpMUSH.Tests.Services;

public class FiveFieldScheduleTests
{
	[Test]
	public async Task DayFieldsUseUnionAndSundayAliasesDeduplicate()
	{
		var schedule = new FiveFieldSchedule("0 12 15 * 0,7", "UTC");
		await Assert.That(schedule.Next(DateTimeOffset.Parse("2026-09-12T13:00:00Z"))).IsEqualTo(DateTimeOffset.Parse("2026-09-13T12:00:00Z"));
		await Assert.That(schedule.Next(DateTimeOffset.Parse("2026-09-13T12:00:00Z"))).IsEqualTo(DateTimeOffset.Parse("2026-09-15T12:00:00Z"));
	}
	[Test]
	public async Task SpringGapSkipsAndAutumnFoldFiresOnlyAtEarlierOccurrence()
	{
		var spring = new FiveFieldSchedule("30 2 * * *", "America/New_York");
		await Assert.That(spring.Next(DateTimeOffset.Parse("2026-03-08T05:00:00Z"))).IsEqualTo(DateTimeOffset.Parse("2026-03-09T06:30:00Z"));
		var autumn = new FiveFieldSchedule("30 1 * * *", "America/New_York");
		await Assert.That(autumn.Next(DateTimeOffset.Parse("2026-11-01T04:00:00Z"))).IsEqualTo(DateTimeOffset.Parse("2026-11-01T05:30:00Z"));
		await Assert.That(autumn.Next(DateTimeOffset.Parse("2026-11-01T06:00:00Z"))).IsEqualTo(DateTimeOffset.Parse("2026-11-02T06:30:00Z"));
	}
	[Test]
	public async Task StepsRangesAndTimezoneChangesAreExplicit()
	{
		var utc = new FiveFieldSchedule("*/15 9-17 * * 1-5", "UTC");
		await Assert.That(utc.Next(DateTimeOffset.Parse("2026-09-14T09:07:00Z"))).IsEqualTo(DateTimeOffset.Parse("2026-09-14T09:15:00Z"));
		var tokyo = new FiveFieldSchedule("0 9 * * *", "Asia/Tokyo");
		await Assert.That(tokyo.Next(DateTimeOffset.Parse("2026-09-14T12:00:00Z"))).IsEqualTo(DateTimeOffset.Parse("2026-09-15T00:00:00Z"));
	}
	[Test]
	[Arguments("0 0 * * ?")]
	[Arguments("0 0 * * MON")]
	[Arguments("0 0 1 * * *")]
	[Arguments("*/0 * * * *")]
	[Arguments("60 * * * *")]
	[Arguments("0 0 10-2 * *")]
	[Arguments("0 0 * * 8")]
	public async Task UnsupportedOrMalformedGrammarIsRejected(string expression)
		=> await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(new FiveFieldSchedule(expression, "UTC")));
	[Test]
	public async Task ImpossibleCalendarDateHasNoFutureOccurrence()
		=> await Assert.That(new FiveFieldSchedule("0 0 31 2 *", "UTC").Next(DateTimeOffset.Parse("2026-01-01T00:00:00Z"))).IsNull();

}
