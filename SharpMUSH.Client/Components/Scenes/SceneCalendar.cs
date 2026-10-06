using System.Globalization;
using System.Text;

namespace SharpMUSH.Client.Components.Scenes;

/// <summary>
/// A scheduled scene as a calendar event: a Google Calendar link, and an iCalendar (.ics) file that Apple
/// Calendar, Outlook and every other calendar app open. A scene has a start and no end, so the event runs
/// for <see cref="Length"/>.
/// </summary>
public static class SceneCalendar
{
	public static readonly TimeSpan Length = TimeSpan.FromHours(2);

	/// <summary>Google Calendar's "create event" page with the scene filled in.</summary>
	public static string GoogleUrl(string title, string details, string location, DateTimeOffset start) =>
		"https://calendar.google.com/calendar/render?action=TEMPLATE"
		+ "&text=" + Uri.EscapeDataString(title)
		+ "&dates=" + Stamp(start) + "/" + Stamp(start + Length)
		+ "&details=" + Uri.EscapeDataString(details)
		+ "&location=" + Uri.EscapeDataString(location);

	/// <summary>
	/// One VEVENT in an iCalendar file (RFC 5545). <paramref name="uid"/> is stable for the scene, so importing
	/// it again after a reschedule updates the event rather than adding a second one.
	/// </summary>
	public static string Ics(string uid, string title, string details, string location, string url,
		DateTimeOffset start, DateTimeOffset now)
	{
		var lines = new[]
		{
			"BEGIN:VCALENDAR",
			"VERSION:2.0",
			"PRODID:-//SharpMUSH//Scenes//EN",
			"CALSCALE:GREGORIAN",
			"METHOD:PUBLISH",
			"BEGIN:VEVENT",
			"UID:" + Text(uid),
			"DTSTAMP:" + Stamp(now),
			"DTSTART:" + Stamp(start),
			"DTEND:" + Stamp(start + Length),
			"SUMMARY:" + Text(title),
			"DESCRIPTION:" + Text(details),
			"LOCATION:" + Text(location),
			"URL:" + url,
			"END:VEVENT",
			"END:VCALENDAR",
		};

		var ics = new StringBuilder();
		foreach (var line in lines)
		{
			Fold(ics, line);
		}

		return ics.ToString();
	}

	/// <summary>The .ics file as a link target: a browser saves it under the anchor's download name.</summary>
	public static string DataUri(string ics) => "data:text/calendar;charset=utf-8," + Uri.EscapeDataString(ics);

	private static string Stamp(DateTimeOffset at) =>
		at.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

	/// <summary>A TEXT value: backslash, semicolon, comma and line breaks are escaped (RFC 5545 §3.3.11).</summary>
	private static string Text(string value) => value
		.Replace("\\", "\\\\")
		.Replace(";", "\\;")
		.Replace(",", "\\,")
		.Replace("\r\n", "\\n")
		.Replace("\n", "\\n")
		.Replace("\r", "\\n");

	/// <summary>Lines longer than 75 octets continue on the next line after a space (RFC 5545 §3.1).</summary>
	private static void Fold(StringBuilder ics, string line)
	{
		var octets = 0;
		var limit = 75;
		foreach (var rune in line.EnumerateRunes())
		{
			var size = rune.Utf8SequenceLength;
			if (octets + size > limit)
			{
				ics.Append("\r\n ");
				octets = 0;
				limit = 74;
			}

			ics.Append(rune.ToString());
			octets += size;
		}

		ics.Append("\r\n");
	}
}
