using Bunit;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

public class LiveAnnouncerTests : TrackingBunitContext
{
	[Test]
	public async Task MountsEmpty_AsAPoliteLabelledLog()
	{
		var cut = Render<LiveAnnouncer>(p => p.Add(x => x.Label, "Messages in Public"));
		var log = cut.Find("[role='log']");
		await Assert.That(log.GetAttribute("aria-live")).IsEqualTo("polite");
		await Assert.That(log.GetAttribute("aria-label")).IsEqualTo("Messages in Public");
		await Assert.That(log.ClassList.Contains("visually-hidden")).IsTrue();
		await Assert.That(log.Children.Length).IsEqualTo(0)
			.Because("a region that mounts with its content is not read; the first line has to arrive into it");
	}

	[Test]
	public async Task KeepsTheLastFive_AndSkipsBlanks()
	{
		var cut = Render<LiveAnnouncer>(p => p.Add(x => x.Label, "Poses"));
		await cut.InvokeAsync(() =>
		{
			cut.Instance.Announce(" ");
			for (var i = 1; i <= 7; i++) cut.Instance.Announce($"line {i}");
		});

		var lines = cut.FindAll("[role='log'] > div").Select(d => d.TextContent).ToArray();
		await Assert.That(lines).IsEquivalentTo(new[] { "line 3", "line 4", "line 5", "line 6", "line 7" });
	}
}
