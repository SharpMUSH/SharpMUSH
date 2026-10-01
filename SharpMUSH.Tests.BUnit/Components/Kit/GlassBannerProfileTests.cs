using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

/// <summary>
/// The banner's character-profile parts (board 25): a lead (the portrait) beside the text, the name
/// in the character's colour, actions at the bottom right, and the hue gradient when there is no image.
/// </summary>
public class GlassBannerProfileTests : BunitContext
{
	public GlassBannerProfileTests()
	{
		Services.AddLocalization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task Lead_RendersBeforeTheText()
	{
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "Tomas Reyes").Add(x => x.ImageUrl, "/b.jpg")
			.Add(x => x.Lead, b => b.AddMarkupContent(0, "<span class=\"portrait\">P</span>")));
		var lead = cut.Find(".kit-banner-lead");
		await Assert.That(lead.QuerySelector(".portrait")).IsNotNull();
		await Assert.That(lead.NextElementSibling!.ClassList).Contains("kit-banner-text");
	}

	[Test]
	public async Task TitleColor_AppliesOnlyAHexColour()
	{
		var ok = Render<GlassBanner>(p => p.Add(x => x.Title, "Tomas").Add(x => x.TitleColor, "#ffb454"));
		await Assert.That(ok.Find(".kit-banner-title").GetAttribute("style")).IsEqualTo("color:#ffb454;");

		var bad = Render<GlassBanner>(p => p.Add(x => x.Title, "Tomas").Add(x => x.TitleColor, "red;background:url(x)"));
		await Assert.That(bad.Find(".kit-banner-title").GetAttribute("style")).IsNull();
	}

	[Test]
	public async Task BottomActions_SitInTheirOwnCorner()
	{
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "Tomas")
			.Add(x => x.BottomActions, b => b.AddMarkupContent(0, "<a class=\"mail\" href=\"/mail\">Mail</a>")));
		await Assert.That(cut.Find(".kit-banner-bottom-actions a.mail")).IsNotNull();
	}

	[Test]
	public async Task Hue_WithoutAnImage_DrawsTheGradient()
	{
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "Tomas").Add(x => x.Hue, 200));
		var banner = cut.Find(".kit-banner");
		await Assert.That(banner.ClassList).Contains("kit-banner--hue");
		await Assert.That(banner.GetAttribute("style")).Contains("--banner-hue:200");
		await Assert.That(cut.FindAll("img.kit-banner-img").Count).IsEqualTo(0);

		var imaged = Render<GlassBanner>(p => p.Add(x => x.Title, "Tomas").Add(x => x.Hue, 200).Add(x => x.ImageUrl, "/b.jpg"));
		await Assert.That(imaged.Find(".kit-banner").ClassList).DoesNotContain("kit-banner--hue");
	}
}
