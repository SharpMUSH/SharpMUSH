using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Play;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Tests.BUnit.Components.Play;

/// <summary>
/// README §5.2 room banner (boards 01–03) from <c>room.info</c>: the picture, the name, "area · N here ·
/// N exits"; Description opens the description over the image; Minimise collapses to the strip.
/// </summary>
public class RoomBannerTests : BunitContext
{
	public RoomBannerTests()
	{
		Services.AddLocalization();
		Services.AddMudServices();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static readonly RoomInfo Docks = new("#1201", "Lower Docks", "#1201:1", "Harbour Ward",
		new ImageRef("/api/wiki-assets/r/docks.jpg", "The quay at dusk", (0.5, 0.6), null, null),
		new RoomDescription("text", "Tarred pilings and stacked crates line the quay.\nLamps are being lit."), null);

	private IRenderedComponent<RoomBanner> RenderBanner(RoomInfo? info, bool minimised = false, Action<bool>? onMinimised = null) =>
		Render<RoomBanner>(p => p.Add(x => x.Info, info).Add(x => x.Here, 5).Add(x => x.Exits, 3)
			.Add(x => x.Minimised, minimised).Add(x => x.MinimisedChanged, m => onMinimised?.Invoke(m)));

	[Test]
	public async Task TheBanner_IsThePicture_TheName_AndTheFacts()
	{
		var cut = RenderBanner(Docks);
		await Assert.That(cut.Find("img.kit-banner-img").GetAttribute("src")).IsEqualTo("/api/wiki-assets/r/docks.jpg");
		await Assert.That(cut.Find("img.kit-banner-img").GetAttribute("alt")).IsEqualTo("The quay at dusk");
		await Assert.That(cut.Find(".kit-banner-title").TextContent).IsEqualTo("Lower Docks");
		await Assert.That(cut.Find(".kit-banner-secondary").TextContent.Trim()).IsEqualTo("Harbour Ward · 5 here · 3 exits");
		await Assert.That(cut.Markup).DoesNotContain("Look").Because("§5.2: no Look button");
	}

	[Test]
	public async Task Description_TogglesTheTextOverTheImage()
	{
		var cut = RenderBanner(Docks);
		var toggle = cut.Find("button.room-banner-desc");
		await Assert.That(toggle.GetAttribute("aria-pressed")).IsEqualTo("false");
		await toggle.ClickAsync();
		await Assert.That(cut.Find(".kit-banner").ClassList).Contains("kit-banner--open");
		await Assert.That(cut.Find(".room-banner-desc-text").TextContent).Contains("Lamps are being lit.");
		await Assert.That(cut.Find("button.room-banner-desc").GetAttribute("aria-pressed")).IsEqualTo("true");
		await cut.Find("button.room-banner-desc").ClickAsync();
		await Assert.That(cut.Find(".kit-banner").ClassList).DoesNotContain("kit-banner--open");
	}

	[Test]
	public async Task WithoutADescription_ThereIsNoDescriptionButton()
	{
		var cut = RenderBanner(Docks with { Desc = null });
		await Assert.That(cut.FindAll("button.room-banner-desc").Count).IsEqualTo(0);
	}

	[Test]
	public async Task WithoutAnArea_TheFactsStartWithTheCount()
	{
		var cut = RenderBanner(Docks with { Area = null });
		await Assert.That(cut.Find(".kit-banner-secondary").TextContent.Trim()).IsEqualTo("5 here · 3 exits");
	}

	[Test]
	public async Task Minimised_IsTheStrip_WithAreaAndHere()
	{
		bool? minimised = null;
		var cut = RenderBanner(Docks, minimised: true, onMinimised: m => minimised = m);
		await Assert.That(cut.Find(".kit-banner-strip-title").TextContent).IsEqualTo("Lower Docks");
		await Assert.That(cut.Find(".kit-banner-strip-fact").TextContent).IsEqualTo("Harbour Ward · 5 here");
		await cut.Find("button.kit-banner-restore").ClickAsync();
		await Assert.That(minimised).IsEqualTo(false);
	}

	[Test]
	public async Task WithoutARoomInfo_NothingRenders()
		=> await Assert.That(RenderBanner(null).Markup.Trim()).IsEmpty();

	[Test]
	public async Task AnUnsafePicture_FallsBackToTheNamesTint()
	{
		var cut = RenderBanner(Docks with { Image = new ImageRef("javascript:alert(1)", null, null, null, null) });
		await Assert.That(cut.FindAll("img").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".kit-banner").ClassList).Contains("kit-banner--hue");
	}
}
