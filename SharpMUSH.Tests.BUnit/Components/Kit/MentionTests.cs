using Bunit;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

public class MentionTests : TrackingBunitContext
{
	[Test]
	public async Task Mention_DefaultsToTheProfileLink_AndSetsNameColour()
	{
		var cut = Render<Mention>(p => p.Add(x => x.Name, "Tomas Reyes").Add(x => x.Color, "#ffb454"));
		var a = cut.Find("a.mention");
		await Assert.That(a.GetAttribute("href")).IsEqualTo("/character/Tomas%20Reyes");
		await Assert.That(a.GetAttribute("style")).Contains("--name:#ffb454");
		await Assert.That(a.TextContent).IsEqualTo("Tomas Reyes");
	}

	[Test]
	public async Task Mention_WithoutColour_SetsNoNameVariable_SoTheUnderlineFallsBackToFaint()
	{
		var cut = Render<Mention>(p => p.Add(x => x.Name, "Wren"));
		await Assert.That(cut.Find("a.mention").GetAttribute("style") ?? "").DoesNotContain("--name");
	}

	[Test]
	public async Task Mention_Self_UsesTheAccent()
	{
		var cut = Render<Mention>(p => p.Add(x => x.Name, "Ilsa").Add(x => x.Self, true));
		await Assert.That(cut.Find("a.mention").GetAttribute("style")).Contains("--name:var(--accent)");
	}

	[Test]
	public async Task Mention_ExplicitHref_Wins()
	{
		var cut = Render<Mention>(p => p.Add(x => x.Name, "Ilsa").Add(x => x.Href, "/wiki/main/people/ilsa"));
		await Assert.That(cut.Find("a.mention").GetAttribute("href")).IsEqualTo("/wiki/main/people/ilsa");
	}

	[Test]
	public async Task Mention_WithOnClickAndNoHref_IsAButton()
	{
		var hit = false;
		var cut = Render<Mention>(p => p.Add(x => x.Name, "Ilsa").Add(x => x.Href, null).Add(x => x.OnClick, () => hit = true));
		await cut.Find("button.mention").ClickAsync();
		await Assert.That(hit).IsTrue();
	}

	[Test]
	public async Task Mention_ChildContent_ReplacesTheDisplayText()
	{
		var cut = Render<Mention>(p => p.Add(x => x.Name, "Tomas Reyes").AddChildContent("Tomas"));
		await Assert.That(cut.Find("a.mention").TextContent).IsEqualTo("Tomas");
		await Assert.That(cut.Find("a.mention").GetAttribute("href")).IsEqualTo("/character/Tomas%20Reyes");
	}

	[Test]
	public async Task OocBand_RendersInitialsTileNameTimeAndText()
	{
		var cut = Render<OocBand>(p => p.Add(x => x.Name, "Wren Halloway").Add(x => x.Color, "#5aa9ff").Add(x => x.Time, "17:08").AddChildContent("brb, making tea"));
		await Assert.That(cut.Find(".kit-ooc-initials").TextContent).IsEqualTo("WH");
		await Assert.That(cut.Find(".kit-ooc-tag").TextContent).IsEqualTo("OOC");
		await Assert.That(cut.Find(".kit-ooc-name").GetAttribute("style")).Contains("#5aa9ff");
		await Assert.That(cut.Find(".kit-ooc-time").TextContent).IsEqualTo("17:08");
		await Assert.That(cut.Find(".kit-ooc-text").TextContent.Trim()).IsEqualTo("brb, making tea");
	}

	[Test]
	public async Task OocBand_OocMarkerIsReadable_OnlyTheInitialsAreDecorative()
	{
		var cut = Render<OocBand>(p => p.Add(x => x.Name, "Wren").AddChildContent("x"));
		await Assert.That(cut.Find(".kit-ooc-tile").HasAttribute("aria-hidden")).IsFalse()
			.Because("hiding the whole tile leaves a screen reader nothing that says this is OOC");
		await Assert.That(cut.Find(".kit-ooc-initials").GetAttribute("aria-hidden")).IsEqualTo("true");
		await Assert.That(cut.Find(".kit-ooc-tag").HasAttribute("aria-hidden")).IsFalse();
	}

	[Test]
	public async Task OocBand_WithoutColourOrTime_OmitsBoth()
	{
		var cut = Render<OocBand>(p => p.Add(x => x.Name, "Wren").AddChildContent("x"));
		await Assert.That(cut.Find(".kit-ooc-name").HasAttribute("style")).IsFalse();
		await Assert.That(cut.FindAll(".kit-ooc-time").Count).IsEqualTo(0);
	}

	[Test]
	[Arguments("red;position:fixed;inset:0;background:black")]
	[Arguments("#5aa9ff;position:fixed")]
	[Arguments("url(https://evil.example/x.png)")]
	public async Task OocBand_DropsANameColourThatIsNotHex(string color)
	{
		var cut = Render<OocBand>(p => p.Add(x => x.Name, "Wren").Add(x => x.Color, color).AddChildContent("x"));
		await Assert.That(cut.Find(".kit-ooc-name").HasAttribute("style")).IsFalse();
	}

	[Test]
	[Arguments("red;position:fixed;inset:0;background:black")]
	[Arguments("#5aa9ff;position:fixed")]
	public async Task Mention_DropsANameColourThatIsNotHex(string color)
	{
		var cut = Render<Mention>(p => p.Add(x => x.Name, "Wren").Add(x => x.Color, color));
		await Assert.That(cut.Find("a.mention").HasAttribute("style")).IsFalse();
	}
}
