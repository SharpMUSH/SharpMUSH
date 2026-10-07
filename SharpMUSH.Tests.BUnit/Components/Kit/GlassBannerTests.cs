using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

public class GlassBannerTests : BunitContext
{
	public GlassBannerTests()
	{
		Services.AddLocalization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task RendersImageBlurLayersScrimAndTitle()
	{
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "Lower Docks").Add(x => x.ImageUrl, "/r.jpg").Add(x => x.Alt, "The quay").Add(x => x.Kicker, "Theme"));
		await Assert.That(cut.Find("img.kit-banner-img").GetAttribute("alt")).IsEqualTo("The quay");
		await Assert.That(cut.FindAll(".kit-banner-blur").Count).IsEqualTo(3);
		await Assert.That(cut.Find(".kit-banner-scrim")).IsNotNull();
		await Assert.That(cut.Find(".kit-banner-title").TextContent).IsEqualTo("Lower Docks");
		await Assert.That(cut.Find(".kit-banner-kicker").TextContent).IsEqualTo("Theme");
		await Assert.That(cut.Find(".kit-banner").GetAttribute("style")).Contains("height:150px");
	}

	[Test]
	public async Task Focal_SetsObjectPosition()
	{
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "x").Add(x => x.ImageUrl, "/r.jpg").Add(x => x.Focal, (0.5, 0.6)));
		await Assert.That(cut.Find("img.kit-banner-img").GetAttribute("style")).Contains("object-position:50% 60%");
	}

	[Test]
	public async Task Open_UsesOpenHeightAndModifier()
	{
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "x").Add(x => x.ImageUrl, "/r.jpg").Add(x => x.Open, true)
			.Add(x => x.OpenContent, b => b.AddMarkupContent(0, "<p class=\"d\">desc</p>")));
		await Assert.That(cut.Find(".kit-banner").ClassList).Contains("kit-banner--open");
		await Assert.That(cut.Find(".kit-banner").GetAttribute("style")).Contains("height:200px");
		await Assert.That(cut.Find(".kit-banner-open .d").TextContent).IsEqualTo("desc");
	}

	[Test]
	public async Task Minimised_ShowsStripWithTitleFactAndRestore()
	{
		var restored = false;
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "Harbour Ward").Add(x => x.ImageUrl, "/r.jpg").Add(x => x.Fact, "5 here")
			.Add(x => x.Minimised, true).Add(x => x.MinimisedChanged, v => restored = !v));
		await Assert.That(cut.Find(".kit-banner-strip .kit-banner-strip-title").TextContent).IsEqualTo("Harbour Ward");
		await Assert.That(cut.Find(".kit-banner-strip .kit-banner-strip-fact").TextContent).IsEqualTo("5 here");
		var restore = cut.Find(".kit-banner-strip button");
		await Assert.That(restore.GetAttribute("aria-label")).IsEqualTo("Show banner");
		await Assert.That(restore.GetAttribute("aria-expanded")).IsEqualTo("false");
		await restore.ClickAsync();
		await Assert.That(restored).IsTrue();
	}

	[Test]
	public async Task MinimiseButton_HasLabelAndExpandedState()
	{
		var minimised = false;
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "x").Add(x => x.ImageUrl, "/r.jpg").Add(x => x.MinimisedChanged, v => minimised = v));
		var btn = cut.Find("button.kit-banner-minimise");
		await Assert.That(btn.GetAttribute("aria-label")).IsEqualTo("Minimise banner");
		await Assert.That(btn.GetAttribute("aria-expanded")).IsEqualTo("true");
		await btn.ClickAsync();
		await Assert.That(minimised).IsTrue();
	}

	[Test]
	public async Task RestoreThenMinimise_KeepsAButtonToFocus()
	{
		// The subtree swaps on Minimised; the counterpart button must exist after the swap so focus can move to it.
		var minimised = true;
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "x").Add(x => x.ImageUrl, "/r.jpg").Add(x => x.Minimised, minimised)
			.Add(x => x.MinimisedChanged, v => minimised = v));
		await cut.Find(".kit-banner-strip button").ClickAsync();
		cut.Render(p => p.Add(x => x.Title, "x").Add(x => x.ImageUrl, "/r.jpg").Add(x => x.Minimised, minimised));
		await Assert.That(cut.Find("button.kit-banner-minimise")).IsNotNull();
		await Assert.That(JSInterop.Invocations.Any(i => i.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase))).IsTrue()
			.Because("after the swap the banner moves focus to the counterpart button");
	}

	[Test]
	public async Task HeadingLevel_RendersTheTitleAsAHeading_InBothStates()
	{
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "Harbour Ward").Add(x => x.ImageUrl, "/r.jpg").Add(x => x.HeadingLevel, 1));
		await Assert.That(cut.Find("h1.kit-banner-title").TextContent).IsEqualTo("Harbour Ward");
		var strip = Render<GlassBanner>(p => p.Add(x => x.Title, "Harbour Ward").Add(x => x.ImageUrl, "/r.jpg").Add(x => x.HeadingLevel, 1).Add(x => x.Minimised, true));
		await Assert.That(strip.Find("h1.kit-banner-strip-title").TextContent).IsEqualTo("Harbour Ward");
	}

	[Test]
	public async Task OpenContent_IsKeyboardScrollable()
	{
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "x").Add(x => x.ImageUrl, "/r.jpg").Add(x => x.Open, true)
			.Add(x => x.OpenContent, b => b.AddMarkupContent(0, "desc")));
		await Assert.That(cut.Find(".kit-banner-open").GetAttribute("tabindex")).IsEqualTo("0");
	}

	[Test]
	public async Task CapsuleButton_Expanded_RendersAriaExpanded_AndAnchorsNeverCarryAriaPressed()
	{
		var btn = Render<CapsuleButton>(p => p.Add(x => x.Icon, MudBlazor.Icons.Material.Outlined.ExpandLess).Add(x => x.AriaLabel, "Minimise").Add(x => x.Expanded, true));
		await Assert.That(btn.Find("button.kit-capsule").GetAttribute("aria-expanded")).IsEqualTo("true");
		var link = Render<CapsuleButton>(p => p.Add(x => x.Href, "/x").Add(x => x.Pressed, true).AddChildContent("x"));
		await Assert.That(link.Find("a.kit-capsule").HasAttribute("aria-pressed")).IsFalse();
	}

	[Test]
	public async Task NotMinimisable_HasNoMinimiseButton()
	{
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "x").Add(x => x.ImageUrl, "/r.jpg").Add(x => x.Minimisable, false));
		await Assert.That(cut.FindAll("button.kit-banner-minimise").Count).IsEqualTo(0);
	}

	[Test]
	public async Task UnsafeImage_RendersNoImgButKeepsTitle()
	{
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "x").Add(x => x.ImageUrl, "javascript:1"));
		await Assert.That(cut.FindAll("img").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".kit-banner").ClassList).Contains("kit-banner--noimage");
		await Assert.That(cut.Find(".kit-banner-title").TextContent).IsEqualTo("x");
	}

	[Test]
	public async Task BackAndActions_RenderInTheirCorners()
	{
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "x").Add(x => x.ImageUrl, "/r.jpg")
			.Add(x => x.Back, b => b.AddMarkupContent(0, "<a class=\"bk\">Back</a>"))
			.Add(x => x.Actions, b => b.AddMarkupContent(0, "<button class=\"ed\">Edit</button>")));
		await Assert.That(cut.Find(".kit-banner-back .bk").TextContent).IsEqualTo("Back");
		await Assert.That(cut.Find(".kit-banner-actions .ed").TextContent).IsEqualTo("Edit");
	}

	[Test]
	public async Task TitleSize_AddsTheSizeModifier()
	{
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "x").Add(x => x.ImageUrl, "/r.jpg").Add(x => x.TitleSize, "hero"));
		await Assert.That(cut.Find(".kit-banner-text").ClassList).Contains("kit-banner-text--hero");
	}

	[Test]
	public async Task CapsuleButton_IconOnly_PrimaryAndPressed()
	{
		var cut = Render<CapsuleButton>(p => p.Add(x => x.Icon, MudBlazor.Icons.Material.Outlined.Notes).Add(x => x.AriaLabel, "Description")
			.Add(x => x.Pressed, true).Add(x => x.Primary, true));
		var b = cut.Find("button.kit-capsule");
		await Assert.That(b.GetAttribute("aria-label")).IsEqualTo("Description");
		await Assert.That(b.GetAttribute("aria-pressed")).IsEqualTo("true");
		await Assert.That(b.ClassList).Contains("kit-capsule--primary");
		await Assert.That(b.ClassList).Contains("kit-capsule--icon");
	}

	[Test]
	public async Task CapsuleButton_WithText_IsNotIconOnly_AndHasNoAriaPressedByDefault()
	{
		var hit = false;
		var cut = Render<CapsuleButton>(p => p.AddChildContent("History").Add(x => x.OnClick, () => hit = true));
		var b = cut.Find("button.kit-capsule");
		await Assert.That(b.ClassList).DoesNotContain("kit-capsule--icon");
		await Assert.That(b.HasAttribute("aria-pressed")).IsFalse();
		await b.ClickAsync();
		await Assert.That(hit).IsTrue();
	}

	[Test]
	public async Task CapsuleButton_WithHref_IsAnAnchor()
	{
		var cut = Render<CapsuleButton>(p => p.Add(x => x.Href, "/wiki").AddChildContent("Back to Wiki"));
		await Assert.That(cut.Find("a.kit-capsule").GetAttribute("href")).IsEqualTo("/wiki");
	}
}
