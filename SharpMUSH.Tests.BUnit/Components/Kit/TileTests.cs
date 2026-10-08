using Bunit;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

public class TileTests : TrackingBunitContext
{
	[Test]
	public async Task PortraitTile_WithImage_RendersImgWithAlt()
	{
		var cut = Render<PortraitTile>(p => p.Add(x => x.Name, "Tomas Reyes").Add(x => x.ImageUrl, "/t.jpg").Add(x => x.Alt, "Tomas at dusk"));
		var img = cut.Find("img.kit-portrait-img");
		await Assert.That(img.GetAttribute("alt")).IsEqualTo("Tomas at dusk");
		await Assert.That(cut.Find(".kit-portrait-label").TextContent.Trim()).IsEqualTo("Tomas Reyes");
	}

	[Test]
	public async Task PortraitTile_WithoutAlt_LeavesTheImageDecorative()
	{
		var cut = Render<PortraitTile>(p => p.Add(x => x.Name, "Tomas Reyes").Add(x => x.ImageUrl, "/t.jpg"));
		await Assert.That(cut.Find("img.kit-portrait-img").GetAttribute("alt")).IsEqualTo("")
			.Because("the label already names the person; alt=Name would announce the name twice");
	}

	[Test]
	public async Task PortraitTile_WithoutImage_ShowsInitialsAtTheSameHeight()
	{
		var cut = Render<PortraitTile>(p => p.Add(x => x.Name, "Dace Kellan"));
		var fb = cut.Find(".kit-portrait-fallback");
		await Assert.That(fb.TextContent.Trim()).IsEqualTo("DK");
		await Assert.That(fb.GetAttribute("style")).Contains("height:72px");
	}

	[Test]
	public async Task PortraitTile_RejectsUnsafeUrl()
	{
		var cut = Render<PortraitTile>(p => p.Add(x => x.Name, "X").Add(x => x.ImageUrl, "http://evil/x.jpg"));
		await Assert.That(cut.FindAll("img").Count).IsEqualTo(0);
	}

	[Test]
	public async Task PortraitTile_Status_DimsAndAppends()
	{
		var cut = Render<PortraitTile>(p => p.Add(x => x.Name, "Wren").Add(x => x.Status, "away"));
		await Assert.That(cut.Find(".kit-portrait-label").TextContent.Trim()).IsEqualTo("Wren · away");
		await Assert.That(cut.Find(".kit-portrait-label").ClassList).Contains("kit-portrait-label--dim");
	}

	[Test]
	public async Task PortraitTile_ClickRunsCallback_AndButtonHasType()
	{
		var hit = false;
		var cut = Render<PortraitTile>(p => p.Add(x => x.Name, "Wren").Add(x => x.OnClick, () => hit = true));
		var b = cut.Find("button.kit-portrait");
		await Assert.That(b.GetAttribute("type")).IsEqualTo("button");
		await b.ClickAsync();
		await Assert.That(hit).IsTrue();
	}

	[Test]
	public async Task PortraitTile_WithHref_IsAnAnchor()
	{
		var cut = Render<PortraitTile>(p => p.Add(x => x.Name, "Wren").Add(x => x.Href, "/character/Wren"));
		await Assert.That(cut.Find("a.kit-portrait").GetAttribute("href")).IsEqualTo("/character/Wren");
	}

	[Test]
	public async Task ImageTile_WithoutImage_RendersIconFill()
	{
		var cut = Render<ImageTile>(p => p.Add(x => x.Label, "Out").Add(x => x.Count, 3));
		await Assert.That(cut.Find(".kit-tile-fallback")).IsNotNull();
		await Assert.That(cut.Find(".kit-tile-count").TextContent).IsEqualTo("3");
	}

	[Test]
	public async Task ImageTile_Height_AppliesToImageAndFallback()
	{
		var cut = Render<ImageTile>(p => p.Add(x => x.Label, "Theme").Add(x => x.ImageUrl, "/t.jpg").Add(x => x.Height, 76));
		await Assert.That(cut.Find("img.kit-tile-img").GetAttribute("style")).Contains("height:76px");
		await Assert.That(cut.Find(".kit-tile-media").GetAttribute("style")).Contains("height:76px");
	}

	[Test]
	public async Task ImageTile_OverlayAndSub_Render()
	{
		var cut = Render<ImageTile>(p => p.Add(x => x.Label, "Customs House").Add(x => x.Sub, "Closed after dusk")
			.Add(x => x.Overlay, b => b.AddMarkupContent(0, "<span class=\"ov\">Locked</span>")));
		await Assert.That(cut.Find(".kit-tile-overlay .ov").TextContent).IsEqualTo("Locked");
		await Assert.That(cut.Find(".kit-tile-sub").TextContent).IsEqualTo("Closed after dusk");
	}

	[Test]
	public async Task ImageTile_RejectsUnsafeUrl()
	{
		var cut = Render<ImageTile>(p => p.Add(x => x.Label, "x").Add(x => x.ImageUrl, "data:image/png;base64,AAAA"));
		await Assert.That(cut.FindAll("img").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".kit-tile-fallback")).IsNotNull();
	}

	[Test]
	public async Task Pill_WithDot_RendersDotInColour()
	{
		var cut = Render<Pill>(p => p.Add(x => x.Dot, "var(--accent)").AddChildContent("In a scene"));
		await Assert.That(cut.Find(".kit-pill-dot").GetAttribute("style")).Contains("var(--accent)");
		await Assert.That(cut.Find(".kit-pill").TextContent.Trim()).IsEqualTo("In a scene");
	}

	[Test]
	public async Task Pill_WithoutDot_HasNoDot()
	{
		var cut = Render<Pill>(p => p.AddChildContent("#312"));
		await Assert.That(cut.FindAll(".kit-pill-dot").Count).IsEqualTo(0);
	}
}
