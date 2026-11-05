using System.Globalization;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

public class DateDisplayTests
{
	/// <summary>A fixed instant in the machine's own zone, so ToLocalTime leaves the wall clock alone.</summary>
	private static DateTimeOffset Local(int year, int month, int day, int hour, int minute)
	{
		var wall = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
		return new DateTimeOffset(wall, TimeZoneInfo.Local.GetUtcOffset(wall));
	}

	[Test]
	[Arguments("en-US", "MMM d, yyyy")]
	[Arguments("de", "d. MMM yyyy")]
	[Arguments("fr", "d MMM yyyy")]
	public async Task The_medium_pattern_is_the_long_one_without_its_weekday(string tag, string expected)
	{
		await Assert.That(DateDisplay.MediumDatePattern(CultureInfo.GetCultureInfo(tag))).IsEqualTo(expected);
	}

	[Test]
	[Arguments("en-US")]
	[Arguments("de")]
	[Arguments("ru")]
	[Arguments("zh-Hans")]
	public async Task No_culture_keeps_a_weekday_or_a_full_month_name(string tag)
	{
		var pattern = DateDisplay.MediumDatePattern(CultureInfo.GetCultureInfo(tag));

		await Assert.That(pattern).DoesNotContain("dddd");
		await Assert.That(pattern).DoesNotContain("MMMM");
	}

	[Test]
	public async Task A_German_date_reads_day_first()
	{
		var culture = CultureInfo.GetCultureInfo("de");
		var when = Local(2026, 11, 5, 16, 36);

		await Assert.That(DateDisplay.Date(when, culture)).StartsWith("5. ");
		await Assert.That(DateDisplay.Date(when, culture)).EndsWith(" 2026");
		await Assert.That(DateDisplay.Time(when, culture)).IsEqualTo("16:36");
	}

	[Test]
	public async Task An_American_date_reads_month_first_with_a_twelve_hour_clock()
	{
		var culture = CultureInfo.GetCultureInfo("en-US");
		var when = Local(2026, 11, 5, 16, 36);

		await Assert.That(DateDisplay.Date(when, culture)).IsEqualTo("Nov 5, 2026");
		await Assert.That(DateDisplay.MonthDay(when, culture)).IsEqualTo("Nov 5");
		await Assert.That(DateDisplay.Time(when, culture)).Contains("4:36");
	}
}
