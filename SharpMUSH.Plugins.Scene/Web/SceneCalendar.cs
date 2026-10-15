using System.Globalization;
using System.Text;

namespace SharpMUSH.Plugins.Scene.Web;

/// <summary>
/// Writes scheduled scenes as an iCalendar feed (RFC 5545) that a calendar app subscribes to.
/// </summary>
/// <remarks>
/// Every time is written in UTC (<c>20261017T061500Z</c>). A calendar app shows a UTC time in the zone of the
/// device it runs on, so each reader sees the scene at their own local time without the feed knowing where they
/// are. A scene has a start and no end, so each event lasts <see cref="DefaultLength"/>.
/// </remarks>
public static class SceneCalendar
{
	/// <summary>How long an event lasts in the calendar: a scene records when it starts, not when it ends.</summary>
	public static readonly TimeSpan DefaultLength = TimeSpan.FromHours(2);

	/// <summary>How often a calendar app should read the feed again.</summary>
	public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);

	/// <summary>The feed's media type.</summary>
	public const string ContentType = "text/calendar; charset=utf-8";

	/// <summary>
	/// The feed for <paramref name="scenes"/>: one event for each scene with a time. <paramref name="portal"/> is the
	/// portal's address, for each event's link; <paramref name="host"/> makes each event's id unique to this game.
	/// </summary>
	public static string Write(IEnumerable<Contracts.Scene> scenes, string calendarName, Uri portal, string host, DateTimeOffset now)
	{
		var feed = new StringBuilder();
		Line(feed, "BEGIN:VCALENDAR");
		Line(feed, "VERSION:2.0");
		Line(feed, "PRODID:-//SharpMUSH//Scene schedule//EN");
		Line(feed, "CALSCALE:GREGORIAN");
		Line(feed, "METHOD:PUBLISH");
		Line(feed, $"X-WR-CALNAME:{Text(calendarName)}");
		Line(feed, $"REFRESH-INTERVAL;VALUE=DURATION:{Duration(RefreshInterval)}");
		Line(feed, $"X-PUBLISHED-TTL:{Duration(RefreshInterval)}");

		foreach (var scene in scenes)
		{
			if (scene.ScheduledFor is not { } start) continue;
			var begins = DateTimeOffset.FromUnixTimeMilliseconds(start);
			var link = new Uri(portal, $"scenes/{Uri.EscapeDataString(scene.Id)}");

			Line(feed, "BEGIN:VEVENT");
			Line(feed, $"UID:scene-{Text(scene.Id)}@{Text(host)}");
			Line(feed, $"DTSTAMP:{Utc(now)}");
			Line(feed, $"LAST-MODIFIED:{Utc(DateTimeOffset.FromUnixTimeMilliseconds(scene.LastActivityAt))}");
			Line(feed, $"DTSTART:{Utc(begins)}");
			Line(feed, $"DTEND:{Utc(begins + DefaultLength)}");
			Line(feed, $"SUMMARY:{Text(Title(scene))}");
			Line(feed, $"DESCRIPTION:{Text(Description(scene, link))}");
			if (!string.IsNullOrWhiteSpace(scene.RoomName)) Line(feed, $"LOCATION:{Text(scene.RoomName)}");
			Line(feed, $"URL:{link.AbsoluteUri}");
			Line(feed, "STATUS:CONFIRMED");
			Line(feed, "TRANSP:TRANSPARENT");
			Line(feed, "END:VEVENT");
		}

		Line(feed, "END:VCALENDAR");
		return feed.ToString();
	}

	private static string Title(Contracts.Scene scene) =>
		scene.Meta.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title) ? title : $"Scene {scene.Id}";

	/// <summary>
	/// The pitch, who runs the scene, then the link, so an app that shows no URL field still offers the way in.
	/// </summary>
	private static string Description(Contracts.Scene scene, Uri link) =>
		string.Join("\n\n", new[]
		{
			scene.Meta.TryGetValue("summary", out var pitch) ? pitch.Trim() : "",
			string.IsNullOrWhiteSpace(scene.OwnerName) ? "" : $"Run by {scene.OwnerName}",
			link.AbsoluteUri
		}.Where(part => part.Length > 0));

	private static string Utc(DateTimeOffset time) =>
		time.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

	private static string Duration(TimeSpan span) => $"PT{(int)span.TotalHours}H";

	/// <summary>A TEXT value (RFC 5545 §3.3.11): backslash, semicolon, comma and line breaks escaped.</summary>
	private static string Text(string value) =>
		value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,")
			.Replace("\r\n", "\\n").Replace("\r", "\\n").Replace("\n", "\\n");

	/// <summary>
	/// Writes one content line, folded so no line is longer than 75 octets (§3.1): each fold is a CRLF and a space,
	/// never inside a UTF-8 sequence.
	/// </summary>
	private static void Line(StringBuilder feed, string line)
	{
		var octets = 0;
		var limit = 75;
		foreach (var rune in line.EnumerateRunes())
		{
			var size = rune.Utf8SequenceLength;
			if (octets + size > limit)
			{
				feed.Append("\r\n ");
				octets = 0;
				limit = 74;
			}
			feed.Append(rune.ToString());
			octets += size;
		}
		feed.Append("\r\n");
	}
}
