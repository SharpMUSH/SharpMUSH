using System.Globalization;

namespace SharpMUSH.Library.Utilities;

/// <summary>
/// The one timestamp shape PennMUSH prints outside of <c>timefmt()</c>.
/// </summary>
public static class TimeFormatting
{
	/// <summary>
	/// PennMUSH <c>show_time()</c>/<c>show_tm()</c> (src/strutil.c:1942): <c>asctime()</c> without its
	/// trailing newline, with the day-of-month zero-padded rather than space-padded. It is what
	/// <c>@uptime</c>, <c>WHO</c>'s idle columns and <c>@channel/recall</c>'s line stamps all print, in
	/// the server's local time zone.
	/// </summary>
	public static string ShowTime(DateTimeOffset when)
		=> when.ToLocalTime().ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture);
}
