using Bunit;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

public class StaticKitTests : BunitContext
{
	[Test]
	public async Task SectionLabel_RendersItsText()
	{
		var cut = Render<SectionLabel>(p => p.AddChildContent("In scene"));
		await Assert.That(cut.Find(".kit-section-label").TextContent.Trim()).IsEqualTo("In scene");
	}

	[Test]
	public async Task KitCard_WithTitle_RendersHeaderRow()
	{
		var cut = Render<KitCard>(p => p.Add(x => x.Title, "Here").Add(x => x.Sub, "4 here").AddChildContent("<p>body</p>"));
		await Assert.That(cut.Find(".kit-card-head .kit-card-title").TextContent).IsEqualTo("Here");
		await Assert.That(cut.Find(".kit-card-head .kit-card-sub").TextContent).IsEqualTo("4 here");
		await Assert.That(cut.Find(".kit-card-body").InnerHtml).Contains("body");
	}

	[Test]
	public async Task KitCard_WithoutTitleOrControls_HasNoHeader()
	{
		var cut = Render<KitCard>(p => p.AddChildContent("x"));
		await Assert.That(cut.FindAll(".kit-card-head").Count).IsEqualTo(0);
	}

	[Test]
	public async Task KitCard_Aside_AddsTheAsideModifier()
	{
		var cut = Render<KitCard>(p => p.Add(x => x.Aside, true).Add(x => x.Title, "Exits · 3"));
		await Assert.That(cut.Find("section").ClassList).Contains("kit-card--aside");
	}

	[Test]
	public async Task PlainPageHeader_DescriptionContent_RendersMarkupInTheDescription()
	{
		var cut = Render<PlainPageHeader>(p => p.Add(x => x.Title, "Sitelock")
			.Add(x => x.DescriptionContent, b => b.AddMarkupContent(0, "Rules: <code>!connect</code>")));
		await Assert.That(cut.Find(".kit-page-desc code").TextContent).IsEqualTo("!connect");
	}

	[Test]
	public async Task PlainPageHeader_RendersKickerTitleDescriptionAndActions()
	{
		var cut = Render<PlainPageHeader>(p => p
			.Add(x => x.Kicker, "Admin · server settings")
			.Add(x => x.Title, "Chat")
			.Add(x => x.Description, "Chat and communication settings")
			.Add(x => x.Actions, b => b.AddMarkupContent(0, "<button>Reset</button>")));
		await Assert.That(cut.Find(".kit-page-kicker").TextContent).IsEqualTo("Admin · server settings");
		await Assert.That(cut.Find("h1").TextContent).IsEqualTo("Chat");
		await Assert.That(cut.Find(".kit-page-desc").TextContent).IsEqualTo("Chat and communication settings");
		await Assert.That(cut.Find(".kit-page-actions button").TextContent).IsEqualTo("Reset");
	}
}
