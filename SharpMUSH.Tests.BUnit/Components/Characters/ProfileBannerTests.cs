using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Tests.BUnit.Components.Characters;

/// <summary>
/// /character/{Name} (board 25): the 236px banner with the portrait, the name in the character's
/// colour, the role line and pills, Mail and Page at the bottom right and Full image at the top. The
/// banner image is the profile's <c>banner</c> field, else the gallery's banner, else the hue gradient;
/// the portrait is the <c>image</c> field, else the gallery's icon, else initials.
/// </summary>
public class ProfileBannerTests : TrackingBunitContext
{
	private readonly CharactersApiFake _fake;

	public ProfileBannerTests()
	{
		(_fake, _, _) = CharactersApiFake.Install(this);
		Services.AddSingleton(CharactersApiFake.Anonymous(this));
		var layouts = Substitute.For<ILayoutService>();
		layouts.GetLayoutAsync(Arg.Any<string>()).Returns(new LayoutConfiguration(new Dictionary<WidgetZone, List<WidgetPlacement>>(), new LayoutSettings(false, false)));
		Services.AddSingleton(layouts);
		Services.AddSingleton<IWidgetRegistry>(new WidgetRegistry());
	}

	private const string TomasProfile = "/http/profile?objid=%23312%3A1";

	private IRenderedComponent<CharacterProfile> RenderProfile(string name = "Tomas Reyes")
	{
		var cut = Render<CharacterProfile>(p => p.Add(x => x.Name, name));
		cut.WaitForAssertion(() => cut.Find(".kit-banner, .char-profile-missing"), TimeSpan.FromSeconds(5));
		return cut;
	}

	[Test]
	public async Task TheProfileFields_DrawTheBanner()
	{
		_fake.Extra[TomasProfile] = """
			{"character":"Tomas Reyes","objid":"#312:1","dbref":"#312",
			 "fields":{"image":"/api/wiki-assets/t/tomas.jpg","banner":"/api/wiki-assets/d/docks.jpg","color":"#ffb454","role":"Lamplighter · Guild of Lamplighters"}}
			""";
		_fake.Extra["/api/scenes?participant=%23312&count=5"] = """
			[{"id":"42","status":"active","isPublic":true,"isTempRoom":false,"startedAt":1,"lastActivityAt":2,"poseCount":12,
			  "ownerName":"Ilsa","starterName":"Ilsa","roomName":"Lower Docks","meta":{"title":"Salt Market at Dusk"}}]
			""";

		var cut = RenderProfile();
		cut.WaitForAssertion(() => cut.Find(".char-profile-pill--scene"), TimeSpan.FromSeconds(5));

		var banner = cut.Find(".kit-banner");
		await Assert.That(banner.GetAttribute("style")).Contains("height:236px");
		await Assert.That(cut.Find("img.kit-banner-img").GetAttribute("src")).IsEqualTo("/api/wiki-assets/d/docks.jpg");
		var title = cut.Find("h1.kit-banner-title");
		await Assert.That(title.TextContent).IsEqualTo("Tomas Reyes");
		await Assert.That(title.GetAttribute("style")).IsEqualTo("color:#ffb454;");
		await Assert.That(cut.Find(".kit-banner-lead img.char-profile-portrait").GetAttribute("src")).IsEqualTo("/api/wiki-assets/t/tomas.jpg");
		await Assert.That(cut.Find(".char-profile-role").TextContent).IsEqualTo("Lamplighter · Guild of Lamplighters");

		var scene = cut.Find("a.char-profile-pill--scene");
		await Assert.That(scene.GetAttribute("href")).IsEqualTo("/play");
		await Assert.That(scene.TextContent).Contains("Salt Market at Dusk");
		await Assert.That(cut.Find(".char-profile-pill--online")).IsNotNull().Because("Tomas is in the online list");
		await Assert.That(cut.Find(".char-profile-pill--dbref").TextContent).IsEqualTo("#312");

		var bottom = cut.Find(".kit-banner-bottom-actions");
		await Assert.That(bottom.QuerySelector("a[href='/mail/compose?to=Tomas%20Reyes']")).IsNotNull();
		await Assert.That(bottom.QuerySelector("a.kit-capsule--primary[href='/play?page=Tomas%20Reyes']")).IsNotNull();
		await Assert.That(cut.Find(".kit-banner-actions button.char-profile-full")).IsNotNull();
	}

	/// <summary>
	/// Paging happens on Play, which a signed-out visitor reaches only as a guest. With no guest to hand
	/// out, Page sent them to "Sorry, there are no guest characters available"; it sends them to sign in.
	/// </summary>
	[Test]
	public async Task Page_ForAVisitorWhoCannotPlayAsAGuest_GoesToSignIn()
	{
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(guestsEnabled: false));
		_fake.Extra[TomasProfile] = """{"character":"Tomas Reyes","objid":"#312:1","dbref":"#312","fields":{}}""";

		var cut = RenderProfile();

		var page = cut.Find(".kit-banner-bottom-actions a.kit-capsule--primary");
		await Assert.That(page.GetAttribute("href")).IsEqualTo("/login?returnUrl=%2Fplay%3Fpage%3DTomas%2520Reyes");
	}

	[Test]
	public async Task WithoutProfileFields_TheGalleryDrawsIt()
	{
		_fake.Extra[TomasProfile] = """{"character":"Tomas Reyes","objid":"#312:1","dbref":"#312","fields":{}}""";
		_fake.Extra["/api/profile/Tomas%20Reyes/gallery"] = """
			[{"assetId":"a","fileName":"a.jpg","url":"/api/wiki-assets/a/a.jpg","caption":"Tomas","order":0,"isIcon":true,"isBanner":false},
			 {"assetId":"b","fileName":"b.jpg","url":"/api/wiki-assets/b/b.jpg","caption":null,"order":1,"isIcon":false,"isBanner":true}]
			""";

		var cut = RenderProfile();
		cut.WaitForAssertion(() => cut.Find("img.kit-banner-img"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find("img.kit-banner-img").GetAttribute("src")).IsEqualTo("/api/wiki-assets/b/b.jpg");
		await Assert.That(cut.Find(".kit-banner-lead img.char-profile-portrait").GetAttribute("src")).IsEqualTo("/api/wiki-assets/a/a.jpg");
	}

	[Test]
	public async Task WithNoImageAtAll_TheHueGradientAndInitials()
	{
		_fake.Extra["/http/profile?objid=%23315%3A1"] = """{"character":"Dace Kellan","objid":"#315:1","dbref":"#315","fields":{}}""";

		var cut = RenderProfile("Dace Kellan");
		cut.WaitForAssertion(() => cut.Find(".char-profile-pill--dbref"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".kit-banner").ClassList).Contains("kit-banner--hue");
		await Assert.That(cut.FindAll("img").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".kit-banner-lead .char-profile-initials").TextContent).IsEqualTo("DK");
		await Assert.That(cut.FindAll(".char-profile-full").Count).IsEqualTo(0).Because("there is no image to show full size");
		await Assert.That(cut.FindAll(".char-profile-pill--online").Count).IsEqualTo(0);
	}

	[Test]
	public async Task Online_IsThisCharactersObjid_NotAnotherWithItsName()
	{
		_fake.Extra[TomasProfile] = """{"character":"Tomas Reyes","objid":"#312:1","dbref":"#312","fields":{}}""";
		_fake.Online = """[{"name":"Tomas Reyes","objid":"#412:1","created":1,"category":""}]""";

		var cut = RenderProfile();
		cut.WaitForAssertion(() => cut.Find(".char-profile-pill--dbref"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".char-profile-pill--online").Count).IsEqualTo(0)
			.Because("the connected Tomas Reyes is #412, not this one");
	}

	[Test]
	public async Task NoSuchCharacter_SaysSo()
	{
		var cut = RenderProfile("Nobody Here");
		await Assert.That(cut.Find(".char-profile-missing").TextContent).Contains("Nobody Here");
		await Assert.That(cut.Find(".char-profile-missing a[href='/characters']")).IsNotNull();
		await Assert.That(cut.FindAll(".kit-banner").Count).IsEqualTo(0);
	}

	[Test]
	public async Task ANameChange_PaintsOnlyTheLatestCharacter()
	{
		// Tomas's profile answers after Dace's: the late answer must not paint over Dace's page.
		var release = new TaskCompletionSource();
		_fake.OnRequest = async request =>
		{
			if (request.RequestUri!.PathAndQuery == TomasProfile) await release.Task;
		};
		_fake.Extra[TomasProfile] = """{"character":"Tomas Reyes","objid":"#312:1","dbref":"#312","fields":{"color":"#ffb454"}}""";
		_fake.Extra["/http/profile?objid=%23315%3A1"] = """{"character":"Dace Kellan","objid":"#315:1","dbref":"#315","fields":{}}""";

		var cut = Render<CharacterProfile>(p => p.Add(x => x.Name, "Tomas Reyes"));
		cut.Render(p => p.Add(x => x.Name, "Dace Kellan"));
		cut.WaitForAssertion(() => cut.Find(".char-profile-pill--dbref"), TimeSpan.FromSeconds(5));
		release.SetResult();
		await Task.Delay(200);
		cut.WaitForAssertion(() => cut.Find("h1.kit-banner-title"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find("h1.kit-banner-title").TextContent).IsEqualTo("Dace Kellan");
		await Assert.That(cut.Find(".char-profile-pill--dbref").TextContent).IsEqualTo("#315");
	}

	[Test]
	public async Task AProfileThatCannotBeRead_IsNotAMissingCharacter()
	{
		// The directory knows Tomas; only the profile hook is gone. That is not "no such character".
		var cut = RenderProfile();
		cut.WaitForAssertion(() => cut.Find(".char-profile-unavailable"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".char-profile-missing").Count).IsEqualTo(0);
		await Assert.That(cut.Find("h1.kit-banner-title").TextContent).IsEqualTo("Tomas Reyes");
	}

	[Test]
	public async Task FullImage_OpensTheViewer()
	{
		_fake.Extra[TomasProfile] = """{"character":"Tomas Reyes","objid":"#312:1","dbref":"#312","fields":{"banner":"/api/wiki-assets/d/docks.jpg"}}""";
		var cut = RenderProfile();
		cut.WaitForAssertion(() => cut.Find("button.char-profile-full"), TimeSpan.FromSeconds(5));
		await cut.Find("button.char-profile-full").ClickAsync();
		await Assert.That(cut.Find("[role='dialog'] img.kit-viewer-img").GetAttribute("src")).IsEqualTo("/api/wiki-assets/d/docks.jpg");
	}
}
