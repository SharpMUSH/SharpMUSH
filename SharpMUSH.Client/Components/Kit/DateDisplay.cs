using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpMUSH.Client.Components.Kit;

/// <summary>
/// Dates and times as the reader's culture writes them, in the reader's time zone. A fixed pattern
/// such as <c>"MMM d, yyyy"</c> translates the month name but keeps English order and punctuation, so
/// German gets "Nov. 5, 2026" instead of "5. Nov. 2026".
/// </summary>
/// <remarks>
/// .NET has short ("d") and long ("D") date patterns but no medium one. <see cref="Date"/> derives it
/// from the long pattern: the weekday dropped and the month abbreviated, so en-US "dddd, MMMM d, yyyy"
/// becomes "MMM d, yyyy" and de "dddd, d. MMMM yyyy" becomes "d. MMM yyyy". Values that are technical
/// rather than read (backup and package timestamps, UTC lines, machine formats) keep their own patterns.
/// </remarks>
public static partial class DateDisplay
{
	/// <summary>"Nov 5, 2026" / "5. Nov. 2026".</summary>
	public static string Date(DateTimeOffset when, CultureInfo? culture = null)
	{
		culture ??= CultureInfo.CurrentCulture;
		return when.ToLocalTime().ToString(MediumDatePattern(culture), culture);
	}

	/// <summary>"Nov 5, 2026, 4:36 PM" / "5. Nov. 2026, 16:36".</summary>
	public static string DateTime(DateTimeOffset when, CultureInfo? culture = null)
	{
		culture ??= CultureInfo.CurrentCulture;
		var local = when.ToLocalTime();
		return $"{local.ToString(MediumDatePattern(culture), culture)}, {local.ToString("t", culture)}";
	}

	/// <summary>"Thursday, November 5, 2026, 4:36 PM" / "Donnerstag, 5. November 2026, 16:36".</summary>
	public static string LongDateTime(DateTimeOffset when, CultureInfo? culture = null)
	{
		culture ??= CultureInfo.CurrentCulture;
		var local = when.ToLocalTime();
		return $"{local.ToString("D", culture)}, {local.ToString("t", culture)}";
	}

	/// <summary>"Nov 5" / "5. Nov.".</summary>
	public static string MonthDay(DateTimeOffset when, CultureInfo? culture = null)
	{
		culture ??= CultureInfo.CurrentCulture;
		return when.ToLocalTime().ToString(Abbreviate(culture.DateTimeFormat.MonthDayPattern), culture);
	}

	/// <summary>"Nov 5, 4:36 PM" / "5. Nov., 16:36".</summary>
	public static string MonthDayTime(DateTimeOffset when, CultureInfo? culture = null)
	{
		culture ??= CultureInfo.CurrentCulture;
		var local = when.ToLocalTime();
		return $"{local.ToString(Abbreviate(culture.DateTimeFormat.MonthDayPattern), culture)}, {local.ToString("t", culture)}";
	}

	/// <summary>"4:36 PM" / "16:36".</summary>
	public static string Time(DateTimeOffset when, CultureInfo? culture = null)
	{
		culture ??= CultureInfo.CurrentCulture;
		return when.ToLocalTime().ToString("t", culture);
	}

	/// <summary>"4:36:12 PM" / "16:36:12".</summary>
	public static string TimeWithSeconds(DateTimeOffset when, CultureInfo? culture = null)
	{
		culture ??= CultureInfo.CurrentCulture;
		return when.ToLocalTime().ToString("T", culture);
	}

	/// <summary>The culture's long date pattern without the weekday, with the month abbreviated.</summary>
	public static string MediumDatePattern(CultureInfo culture)
	{
		var withoutWeekday = Weekday().Replace(culture.DateTimeFormat.LongDatePattern, string.Empty).Trim();
		return Abbreviate(withoutWeekday);
	}

	private static string Abbreviate(string pattern) => FullMonth().Replace(pattern, "MMM");

	// "dddd" with the separator that joins it to the rest: "dddd, " (en), "dddd " (fr), "dddd" at the end (zh).
	[GeneratedRegex(@"dddd[,\s]*|[,\s]*dddd")]
	private static partial Regex Weekday();

	[GeneratedRegex("MMMM")]
	private static partial Regex FullMonth();
}
