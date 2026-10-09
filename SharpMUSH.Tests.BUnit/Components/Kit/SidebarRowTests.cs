using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

public class SidebarRowTests : TrackingBunitContext
{
	public SidebarRowTests()
	{
		Services.AddLocalization();
	}

	[Test]
	public async Task WithHref_RendersAnAnchor_AndCurrentSetsAriaCurrent()
	{
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Salt Market").Add(x => x.Href, "/scenes/42").Add(x => x.IsCurrent, true));
		var a = cut.Find("a.kit-row");
		await Assert.That(a.GetAttribute("href")).IsEqualTo("/scenes/42");
		await Assert.That(a.GetAttribute("aria-current")).IsEqualTo("page");
		await Assert.That(a.ClassList).Contains("kit-row--current");
	}

	[Test]
	public async Task WithoutHref_RendersAButton_ThatRaisesOnClick()
	{
		var clicked = false;
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Public").Add(x => x.OnClick, () => clicked = true));
		var b = cut.Find("button.kit-row");
		await Assert.That(b.GetAttribute("type")).IsEqualTo("button");
		await b.ClickAsync();
		await Assert.That(clicked).IsTrue();
	}

	[Test]
	public async Task ImageLead_RendersImg_OnlyForARenderableUrl()
	{
		var ok = Render<SidebarRow>(p => p.Add(x => x.Label, "x").Add(x => x.Lead, SidebarRow.SidebarLead.Image).Add(x => x.ImageUrl, "/a.jpg").Add(x => x.Name, "Lower Docks"));
		await Assert.That(ok.Find("img.kit-row-img").GetAttribute("src")).IsEqualTo("/a.jpg");
		var bad = Render<SidebarRow>(p => p.Add(x => x.Label, "x").Add(x => x.Lead, SidebarRow.SidebarLead.Image).Add(x => x.ImageUrl, "javascript:1").Add(x => x.Name, "Lower Docks"));
		await Assert.That(bad.FindAll("img").Count).IsEqualTo(0);
		await Assert.That(bad.Find(".kit-row-fallback")).IsNotNull();
	}

	[Test]
	public async Task ImageLead_WithoutImage_ShowsTheIconWhenGiven_ElseInitials()
	{
		// Board 21/23: a wiki page or category with no picture leads with a document icon, not initials.
		var icon = Render<SidebarRow>(p => p.Add(x => x.Label, "Code of Conduct").Add(x => x.Lead, SidebarRow.SidebarLead.Image)
			.Add(x => x.Name, "Code of Conduct").Add(x => x.Icon, MudBlazor.Icons.Material.Outlined.Description));
		var fb = icon.Find(".kit-row-img.kit-row-fallback");
		await Assert.That(fb.QuerySelector("svg")).IsNotNull();
		await Assert.That(fb.TextContent.Trim()).IsEmpty();

		var initials = Render<SidebarRow>(p => p.Add(x => x.Label, "Code of Conduct").Add(x => x.Lead, SidebarRow.SidebarLead.Image).Add(x => x.Name, "Code of Conduct"));
		await Assert.That(initials.Find(".kit-row-img.kit-row-fallback").TextContent.Trim()).IsEqualTo("CO");
	}

	[Test]
	public async Task Trail_ShowsAtTheEnd_WhenThereIsNoCount()
	{
		var trail = Render<SidebarRow>(p => p.Add(x => x.Label, "Ilsa Varn").Add(x => x.Trail, "you"));
		await Assert.That(trail.Find(".kit-row-trail").TextContent).IsEqualTo("you");

		var counted = Render<SidebarRow>(p => p.Add(x => x.Label, "Theme").Add(x => x.Trail, "you").Add(x => x.Count, 3));
		await Assert.That(counted.FindAll(".kit-row-trail").Count).IsEqualTo(0).Because("a count and a word would crowd the row");

		var collapsed = Render<SidebarRow>(p => p.Add(x => x.Label, "Ilsa Varn").Add(x => x.Trail, "you").Add(x => x.Collapsed, true));
		await Assert.That(collapsed.FindAll(".kit-row-trail").Count).IsEqualTo(0);
	}

	[Test]
	public async Task AvatarLead_WithoutImage_ShowsInitialsOnATintedFill()
	{
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Dace Kellan").Add(x => x.Lead, SidebarRow.SidebarLead.Avatar).Add(x => x.Name, "Dace Kellan"));
		var fb = cut.Find(".kit-row-avatar.kit-row-fallback");
		await Assert.That(fb.TextContent.Trim()).IsEqualTo("DK");
		await Assert.That(fb.GetAttribute("style")).Contains("hsl(");
	}

	[Test]
	public async Task GroupAvatar_StacksTwoImages()
	{
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Tomas, Dace").Add(x => x.Lead, SidebarRow.SidebarLead.Avatar).Add(x => x.ImageUrl, "/t.jpg").Add(x => x.ImageUrl2, "/d.jpg"));
		await Assert.That(cut.FindAll("img.kit-row-avatar").Count).IsEqualTo(2);
		await Assert.That(cut.Find(".kit-row-lead").ClassList).Contains("kit-row-lead--group");
	}

	[Test]
	public async Task GroupAvatar_WithoutASecondImage_ShowsTheSecondPersonsInitials()
	{
		// Board 01's "Tomas, Dace": Dace has no picture, so the stacked second avatar is "DK".
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Tomas, Dace").Add(x => x.Lead, SidebarRow.SidebarLead.Avatar)
			.Add(x => x.ImageUrl, "/t.jpg").Add(x => x.Name, "Tomas Reyes").Add(x => x.Name2, "Dace Kellan"));
		await Assert.That(cut.Find(".kit-row-lead").ClassList).Contains("kit-row-lead--group");
		await Assert.That(cut.Find(".kit-row-avatar--second").TextContent).IsEqualTo("DK");
	}

	[Test]
	public async Task ChannelLead_Collapsed_ShowsTheHashAndTheFirstLetter()
	{
		// Board 07: the collapsed strip tells channels apart as #P, #N, #L.
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Public").Add(x => x.Lead, SidebarRow.SidebarLead.Channel).Add(x => x.Collapsed, true));
		await Assert.That(cut.Find(".kit-row-hash").TextContent).IsEqualTo("#P");
		var open = Render<SidebarRow>(p => p.Add(x => x.Label, "Public").Add(x => x.Lead, SidebarRow.SidebarLead.Channel));
		await Assert.That(open.Find(".kit-row-hash").TextContent).IsEqualTo("#");
	}

	[Test]
	public async Task GroupAvatar_WithARejectedSecondImage_StillStacksAFallback()
	{
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Tomas, Dace").Add(x => x.Lead, SidebarRow.SidebarLead.Avatar)
			.Add(x => x.ImageUrl, "/t.jpg").Add(x => x.ImageUrl2, "javascript:1"));
		await Assert.That(cut.Find(".kit-row-lead").ClassList).Contains("kit-row-lead--group");
		await Assert.That(cut.FindAll("img.kit-row-avatar--second").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".kit-row-avatar--second.kit-row-fallback").TextContent).IsEqualTo("?");
	}

	[Test]
	public async Task Unread_RendersAPill_AndBoldsTheRow()
	{
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Public").Add(x => x.Lead, SidebarRow.SidebarLead.Channel).Add(x => x.Unread, 3));
		await Assert.That(cut.Find(".kit-row-unread").TextContent).IsEqualTo("3");
		await Assert.That(cut.Find(".kit-row").ClassList).Contains("kit-row--unread");
	}

	[Test]
	public async Task Sub_RendersADimSecondLine_AndClassLandsOnTheRow()
	{
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Harbour Ward").Add(x => x.Sub, "Wren · 2h ago").Add(x => x.Class, "wiki-recent-row").Add(x => x.Href, "/wiki/main/lore/harbour"));
		await Assert.That(cut.Find(".kit-row-sub").TextContent).IsEqualTo("Wren · 2h ago");
		await Assert.That(cut.Find(".kit-row").ClassList).Contains("wiki-recent-row");
		await Assert.That(cut.Find(".kit-row").ClassList).Contains("kit-row--two-line");
	}

	[Test]
	public async Task Count_RendersDimCount()
	{
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Theme").Add(x => x.Count, 12));
		await Assert.That(cut.Find(".kit-row-count").TextContent).IsEqualTo("12");
	}

	[Test]
	public async Task Collapsed_HidesTheLabelButKeepsItAsTitle()
	{
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Theme").Add(x => x.Collapsed, true));
		await Assert.That(cut.FindAll(".kit-row-label").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".kit-row").GetAttribute("title")).IsEqualTo("Theme");
		await Assert.That(cut.Find(".kit-row").GetAttribute("aria-label")).IsEqualTo("Theme");
	}

	[Test]
	public async Task Collapsed_WithUnread_StillShowsTheBadge()
	{
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Public").Add(x => x.Lead, SidebarRow.SidebarLead.Channel).Add(x => x.Unread, 2).Add(x => x.Collapsed, true));
		await Assert.That(cut.Find(".kit-row-unread--badge").TextContent).IsEqualTo("2");
		await Assert.That(cut.Find(".kit-row").GetAttribute("aria-label")).IsEqualTo("Public, 2 unread")
			.Because("aria-label replaces the accessible name, so the badge must be folded into it");
	}

	[Test]
	public async Task Unread_PillCarriesAnAccessibleCount()
	{
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Public").Add(x => x.Unread, 3));
		await Assert.That(cut.Find(".kit-row-unread").GetAttribute("aria-hidden")).IsEqualTo("true");
		await Assert.That(cut.Find(".kit-row-unread + .visually-hidden").TextContent).IsEqualTo("3 unread")
			.Because("aria-label on a plain span is not read, so the words are hidden text beside the number");
	}
}
