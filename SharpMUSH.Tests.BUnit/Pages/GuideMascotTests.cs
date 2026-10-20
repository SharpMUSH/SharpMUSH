using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Components.Onboarding;
using SharpMUSH.Client.Resources;
using SharpMUSH.Library.Models.Portal.Onboarding;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

public class GuideMascotTests : TrackingBunitContext
{
	[Test]
	public async Task Every_style_keeps_an_approved_mascot_until_its_art_is_available()
	{
		foreach (var style in SharpieArtCatalog.Styles)
		{
			var art = SharpieArtCatalog.Resolve(style, "Fantasy", "unknown", null, GuideImagery.Full);
			await Assert.That(art).IsNotNull();
			await Assert.That(SharpieArtCatalog.Approved.Contains(art!)).IsTrue();
			await Assert.That(SharpieArtCatalog.Resolve(style, null, null, null, GuideImagery.None)).IsNull();
		}
	}

	[Test]
	public async Task Lesson_card_shows_Sharpie_by_default()
	{
		Services.AddSingleton<IStringLocalizer<SharedResource>>(PortalLocalizer.Create());
		var cut = Render<GuidePanel>(p => p.Add(x => x.Lesson, GuideCatalog.Player(GuidePath.Discord)[0]));
		await Assert.That(cut.FindAll("img").Count).IsEqualTo(1);
		await Assert.That(cut.Markup).Contains("help say");
		await Assert.That(cut.Find(".guide-panel-heading button").TextContent).IsEqualTo("Stop this guide");
		await Assert.That(cut.Markup).Contains("Read how a specific command works");
		await Assert.That(cut.Find("img").GetAttribute("src")).Contains("sharpie-minimal.svg");
	}
	[Test]
	public async Task No_artwork_hides_the_mascot_and_reduced_keeps_it_small()
	{
		Services.AddSingleton<IStringLocalizer<SharedResource>>(PortalLocalizer.Create());
		var hidden = Render<GuidePanel>(p => p.Add(x => x.Lesson, GuideCatalog.Player(GuidePath.Discord)[0]).Add(x => x.Imagery, GuideImagery.None));
		await Assert.That(hidden.FindAll("img").Count).IsEqualTo(0);
		var reduced = Render<GuidePanel>(p => p.Add(x => x.Lesson, GuideCatalog.Player(GuidePath.Discord)[0]).Add(x => x.Imagery, GuideImagery.Reduced));
		await Assert.That(reduced.FindAll("img.guide-mascot-reduced").Count).IsEqualTo(1);
	}

}
