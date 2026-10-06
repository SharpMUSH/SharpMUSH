using SharpMUSH.Client.Components.Scenes;

namespace SharpMUSH.Tests.BUnit.Components.Scenes;

/// <summary>The iCalendar text a scheduled scene downloads as: RFC 5545 escaping and line folding.</summary>
public class SceneCalendarTests
{
	private static readonly DateTimeOffset Start = new(2030, 3, 4, 18, 30, 0, TimeSpan.Zero);

	[Test]
	public async Task TextValues_AreEscaped()
	{
		var ics = SceneCalendar.Ics("scene-1@game", "Masks, music; more", "Line one\nLine two \\ end", "The Square", "https://game/scenes/1",
			Start, Start);

		await Assert.That(ics).Contains("SUMMARY:Masks\\, music\\; more\r\n");
		await Assert.That(ics).Contains("DESCRIPTION:Line one\\nLine two \\\\ end\r\n");
		await Assert.That(ics).Contains("DTEND:20300304T203000Z\r\n").Because("a scene runs two hours on a calendar");
		await Assert.That(ics).StartsWith("BEGIN:VCALENDAR\r\n");
		await Assert.That(ics).EndsWith("END:VCALENDAR\r\n");
	}

	[Test]
	public async Task LongLines_AreFoldedAt75Octets()
	{
		var ics = SceneCalendar.Ics("scene-1@game", new string('é', 60), "", "", "https://game/scenes/1", Start, Start);

		var lines = ics.Split("\r\n");
		await Assert.That(lines.All(l => System.Text.Encoding.UTF8.GetByteCount(l) <= 75)).IsTrue();
		var summary = string.Concat(lines.SkipWhile(l => !l.StartsWith("SUMMARY:")).TakeWhile(l => l.StartsWith("SUMMARY:") || l.StartsWith(' '))
			.Select(l => l.StartsWith(' ') ? l[1..] : l));
		await Assert.That(summary).IsEqualTo("SUMMARY:" + new string('é', 60)).Because("unfolding gives the line back, no character split");
	}
}
