using SharpMUSH.Plugins.Scene.Web;
using Scene = SharpMUSH.Plugins.Scene.Models.Scene;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// The scene schedule as an iCalendar feed: times in UTC so each calendar app shows its own zone, TEXT values
/// escaped, and long lines folded at 75 octets.
/// </summary>
public class SceneCalendarTests
{
	private static readonly Uri Portal = new("https://game.example/");
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 6, 0, 0, TimeSpan.Zero);

	private static Scene Scheduled(string id, DateTimeOffset? at, Dictionary<string, string>? meta = null, string room = "The Lantern Inn") =>
		SceneFixture.SceneOwnedBy("#1", isPublic: true) with
		{
			Id = id,
			Status = "scheduled",
			ScheduledFor = at?.ToUnixTimeMilliseconds(),
			LastActivityAt = Now.ToUnixTimeMilliseconds(),
			RoomName = room,
			Meta = meta ?? new Dictionary<string, string> { ["title"] = "The Northern Road" }
		};

	/// <summary>Unfolds the feed back into its content lines (RFC 5545 §3.1).</summary>
	private static string[] Lines(string feed) => feed.Replace("\r\n ", "").Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

	[Test]
	public async Task AScheduledScene_IsAnEventStartingAtItsTimeInUtc()
	{
		// 20:15 in UTC+2 is 18:15 UTC: the feed names the instant, and each calendar shows its own zone.
		var at = new DateTimeOffset(2026, 10, 17, 20, 15, 0, TimeSpan.FromHours(2));

		var lines = Lines(SceneCalendar.Write([Scheduled("2", at)], "Lantern scenes", Portal, "game.example", Now));

		await Assert.That(lines).Contains("DTSTART:20261017T181500Z");
		await Assert.That(lines).Contains("DTEND:20261017T201500Z");
		await Assert.That(lines).Contains("SUMMARY:The Northern Road");
		await Assert.That(lines).Contains("UID:scene-2@game.example");
		await Assert.That(lines).Contains("URL:https://game.example/scenes/2");
		await Assert.That(lines).Contains("LOCATION:The Lantern Inn");
		await Assert.That(lines).Contains("X-WR-CALNAME:Lantern scenes");
		await Assert.That(lines).Contains("DTSTAMP:20261015T060000Z");
		await Assert.That(lines.First()).IsEqualTo("BEGIN:VCALENDAR");
		await Assert.That(lines.Last()).IsEqualTo("END:VCALENDAR");
	}

	[Test]
	public async Task ASceneWithNoTime_IsLeftOut()
	{
		var lines = Lines(SceneCalendar.Write([Scheduled("4", null)], "Scenes", Portal, "game.example", Now));

		await Assert.That(lines).DoesNotContain("BEGIN:VEVENT");
	}

	[Test]
	public async Task TextValues_AreEscaped()
	{
		var meta = new Dictionary<string, string>
		{
			["title"] = "Storms, goats; and \\ more",
			["summary"] = "Line one\nLine two"
		};

		var lines = Lines(SceneCalendar.Write([Scheduled("5", Now.AddDays(1), meta)], "Scenes", Portal, "game.example", Now));

		await Assert.That(lines).Contains("SUMMARY:Storms\\, goats\\; and \\\\ more");
		await Assert.That(lines).Contains("DESCRIPTION:Line one\\nLine two\\n\\nRun by God\\n\\nhttps://game.example/scenes/5");
	}

	[Test]
	public async Task LongLines_AreFoldedAt75Octets_NeverInsideACharacter()
	{
		var meta = new Dictionary<string, string> { ["title"] = string.Concat(Enumerable.Repeat("Ærøskøbing ", 20)) };

		var feed = SceneCalendar.Write([Scheduled("6", Now.AddDays(1), meta)], "Scenes", Portal, "game.example", Now);
		var physical = feed.Split("\r\n");

		await Assert.That(physical.Max(line => System.Text.Encoding.UTF8.GetByteCount(line))).IsLessThanOrEqualTo(75);
		await Assert.That(physical.Any(line => line.Contains('�'))).IsFalse();
		await Assert.That(Lines(feed)).Contains($"SUMMARY:{meta["title"]}");
	}

	[Test]
	public async Task AnUntitledScene_IsNamedByItsNumber()
	{
		var lines = Lines(SceneCalendar.Write([Scheduled("7", Now.AddDays(1), [])], "Scenes", Portal, "game.example", Now));

		await Assert.That(lines).Contains("SUMMARY:Scene 7");
	}
}
