using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Widgets;
using AccountCharacter = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

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
	private readonly BunitAuthorizationContext _auth;

	public ProfileBannerTests()
	{
		(_fake, _, _auth) = CharactersApiFake.Install(this);
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

	private static IRenderedComponent<InputFile> UploadInput(IRenderedComponent<CharacterProfile> cut, string upload)
	{
		var id = cut.Find($"{upload} input[type=file]").Id;
		return cut.FindComponents<InputFile>().First(i => i.Find("input").Id == id);
	}

	private async Task SignInAsTomasAsync(bool wikiEdit = true)
	{
		_auth.SetAuthorized("player");
		if (wikiEdit) _auth.SetClaims(new System.Security.Claims.Claim(PortalPermission.ClaimType, PortalPermission.WikiEdit));
		Services.AddSingleton(await CharactersApiFake.SignedInAsync(this, new AccountCharacter(312, 1, "Tomas Reyes", "PLAYER", IsActing: true)));
	}

	/// <summary>Owning the character is not enough: its images change only with wiki.edit.</summary>
	[Test]
	public async Task TheOwner_WithoutWikiEdit_IsNotOfferedTheImageControls()
	{
		await SignInAsTomasAsync(wikiEdit: false);
		_fake.Extra[TomasProfile] = """{"character":"Tomas Reyes","objid":"#312:1","dbref":"#312","fields":{}}""";
		var cut = RenderProfile();
		cut.WaitForAssertion(() => cut.Find(".char-profile-pill--dbref"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".char-profile-change-banner").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".char-profile-change-avatar").Count).IsEqualTo(0);
	}

	[Test]
	public async Task AVisitor_IsNotOfferedTheImageControls()
	{
		_fake.Extra[TomasProfile] = """{"character":"Tomas Reyes","objid":"#312:1","dbref":"#312","fields":{}}""";
		var cut = RenderProfile();
		cut.WaitForAssertion(() => cut.Find(".char-profile-pill--dbref"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".char-profile-change-banner").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".char-profile-change-avatar").Count).IsEqualTo(0);
	}

	[Test]
	public async Task TheOwner_ChangesTheBanner_AndItIsNotTheAvatar()
	{
		await SignInAsTomasAsync();
		_fake.Extra[TomasProfile] = """{"character":"Tomas Reyes","objid":"#312:1","dbref":"#312","fields":{"image":"/api/wiki-assets/a/a.jpg"}}""";
		_fake.Extra["/api/profile/Tomas%20Reyes/gallery"] = """
			[{"assetId":"a","fileName":"a.jpg","url":"/api/wiki-assets/a/a.jpg","caption":null,"order":0,"isIcon":true,"isBanner":false}]
			""";
		_fake.Extra["POST /api/profile/Tomas%20Reyes/gallery?use=banner"] = """
			[{"assetId":"a","fileName":"a.jpg","url":"/api/wiki-assets/a/a.jpg","caption":null,"order":0,"isIcon":true,"isBanner":false},
			 {"assetId":"d","fileName":"docks.jpg","url":"/api/wiki-assets/d/docks.jpg","caption":null,"order":1,"isIcon":false,"isBanner":true}]
			""";

		var cut = RenderProfile();
		cut.WaitForAssertion(() => cut.Find(".char-profile-change-banner"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".char-profile-remove-banner").Count).IsEqualTo(0).Because("there is no banner to remove yet");

		await cut.Find(".char-profile-change-banner").ClickAsync();
		cut.WaitForAssertion(() => cut.Find("[role='dialog'] .kit-picker-upload"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".kit-picker-title").TextContent).IsEqualTo("Change banner");
		UploadInput(cut, ".kit-picker-upload")
			.UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "docks.jpg", contentType: "image/jpeg"));

		cut.WaitForAssertion(() => cut.Find("img.kit-banner-img"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll("[role='dialog']").Count).IsEqualTo(0).Because("the picker closes once a file is chosen");
		await Assert.That(cut.Find("img.kit-banner-img").GetAttribute("src")).IsEqualTo("/api/wiki-assets/d/docks.jpg");
		await Assert.That(cut.Find(".kit-banner-lead img.char-profile-portrait").GetAttribute("src")).IsEqualTo("/api/wiki-assets/a/a.jpg");
		await Assert.That(cut.FindAll(".char-profile-remove-banner").Count).IsEqualTo(1);
	}

	[Test]
	public async Task TheOwner_ChangesTheAvatar_AndTheBannerStays()
	{
		await SignInAsTomasAsync();
		_fake.Extra[TomasProfile] = """{"character":"Tomas Reyes","objid":"#312:1","dbref":"#312","fields":{"banner":"/api/wiki-assets/d/docks.jpg"}}""";
		_fake.Extra["/api/profile/Tomas%20Reyes/gallery"] = """
			[{"assetId":"d","fileName":"docks.jpg","url":"/api/wiki-assets/d/docks.jpg","caption":null,"order":0,"isIcon":false,"isBanner":true}]
			""";
		_fake.Extra["POST /api/profile/Tomas%20Reyes/gallery?use=avatar"] = """
			[{"assetId":"d","fileName":"docks.jpg","url":"/api/wiki-assets/d/docks.jpg","caption":null,"order":0,"isIcon":false,"isBanner":true},
			 {"assetId":"t","fileName":"tomas.jpg","url":"/api/wiki-assets/t/tomas.jpg","caption":null,"order":1,"isIcon":true,"isBanner":false}]
			""";

		var cut = RenderProfile();
		cut.WaitForAssertion(() => cut.Find(".char-profile-change-avatar"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".kit-banner-lead .char-profile-initials").TextContent).IsEqualTo("TR")
			.Because("a gallery of only the banner has no avatar");

		await cut.Find(".char-profile-change-avatar").ClickAsync();
		cut.WaitForAssertion(() => cut.Find("[role='dialog'] .kit-picker-upload"), TimeSpan.FromSeconds(5));
		UploadInput(cut, ".kit-picker-upload")
			.UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "tomas.jpg", contentType: "image/jpeg"));

		cut.WaitForAssertion(() => cut.Find(".kit-banner-lead img.char-profile-portrait"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".kit-banner-lead img.char-profile-portrait").GetAttribute("src")).IsEqualTo("/api/wiki-assets/t/tomas.jpg");
		await Assert.That(cut.Find("img.kit-banner-img").GetAttribute("src")).IsEqualTo("/api/wiki-assets/d/docks.jpg");
	}

	[Test]
	public async Task TheOwner_PicksTheAvatarFromTheGallery()
	{
		await SignInAsTomasAsync();
		_fake.Extra[TomasProfile] = """{"character":"Tomas Reyes","objid":"#312:1","dbref":"#312","fields":{}}""";
		_fake.Extra["/api/profile/Tomas%20Reyes/gallery"] = """
			[{"assetId":"d","fileName":"docks.jpg","url":"/api/wiki-assets/d/docks.jpg","caption":"Docks","order":0,"isIcon":false,"isBanner":true},
			 {"assetId":"t","fileName":"tomas.jpg","url":"/api/wiki-assets/t/tomas.jpg","caption":"Tomas","order":1,"isIcon":false,"isBanner":false}]
			""";
		string? sent = null;
		_fake.OnRequest = async request =>
		{
			if (request.Method == HttpMethod.Put) sent = await request.Content!.ReadAsStringAsync();
		};
		_fake.Extra["PUT /api/profile/Tomas%20Reyes/gallery"] = """
			[{"assetId":"d","fileName":"docks.jpg","url":"/api/wiki-assets/d/docks.jpg","caption":"Docks","order":0,"isIcon":false,"isBanner":true},
			 {"assetId":"t","fileName":"tomas.jpg","url":"/api/wiki-assets/t/tomas.jpg","caption":"Tomas","order":1,"isIcon":true,"isBanner":false}]
			""";

		var cut = RenderProfile();
		cut.WaitForAssertion(() => cut.Find(".char-profile-change-avatar"), TimeSpan.FromSeconds(5));
		await cut.Find(".char-profile-change-avatar").ClickAsync();
		cut.WaitForAssertion(() => cut.Find("[role='dialog'] .kit-picker-item"), TimeSpan.FromSeconds(5));

		var items = cut.FindAll(".kit-picker-item");
		await Assert.That(items.Count).IsEqualTo(2);
		await Assert.That(items.All(i => i.GetAttribute("aria-pressed") == "false")).IsTrue()
			.Because("nothing is the avatar yet; the banner is not marked as one");
		await cut.Find(".kit-picker-item[aria-label='Tomas']").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".kit-banner-lead img.char-profile-portrait"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".kit-banner-lead img.char-profile-portrait").GetAttribute("src")).IsEqualTo("/api/wiki-assets/t/tomas.jpg");
		await Assert.That(sent).Contains("\"assetId\":\"t\"");
		await Assert.That(System.Text.Json.JsonDocument.Parse(sent!).RootElement.EnumerateArray()
			.Single(e => e.GetProperty("isIcon").GetBoolean()).GetProperty("assetId").GetString()).IsEqualTo("t");
		await Assert.That(cut.FindAll("[role='dialog']").Count).IsEqualTo(0);
	}

	[Test]
	public async Task ThePicker_MarksTheCurrentBanner()
	{
		await SignInAsTomasAsync();
		_fake.Extra[TomasProfile] = """{"character":"Tomas Reyes","objid":"#312:1","dbref":"#312","fields":{}}""";
		_fake.Extra["/api/profile/Tomas%20Reyes/gallery"] = """
			[{"assetId":"d","fileName":"docks.jpg","url":"/api/wiki-assets/d/docks.jpg","caption":"Docks","order":0,"isIcon":false,"isBanner":true},
			 {"assetId":"t","fileName":"tomas.jpg","url":"/api/wiki-assets/t/tomas.jpg","caption":"Tomas","order":1,"isIcon":true,"isBanner":false}]
			""";

		var cut = RenderProfile();
		cut.WaitForAssertion(() => cut.Find(".char-profile-change-banner"), TimeSpan.FromSeconds(5));
		await cut.Find(".char-profile-change-banner").ClickAsync();
		cut.WaitForAssertion(() => cut.Find("[role='dialog'] .kit-picker-item"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".kit-picker-item--current").GetAttribute("aria-label")).IsEqualTo("Docks");

		await cut.Find(".kit-picker-close").ClickAsync();
		await Assert.That(cut.FindAll("[role='dialog']").Count).IsEqualTo(0);
	}

	[Test]
	public async Task TheOwner_RemovesTheBanner()
	{
		await SignInAsTomasAsync();
		_fake.Extra[TomasProfile] = """{"character":"Tomas Reyes","objid":"#312:1","dbref":"#312","fields":{"banner":"/api/wiki-assets/d/docks.jpg"}}""";
		_fake.Extra["/api/profile/Tomas%20Reyes/gallery"] = """
			[{"assetId":"d","fileName":"docks.jpg","url":"/api/wiki-assets/d/docks.jpg","caption":null,"order":0,"isIcon":false,"isBanner":true}]
			""";
		string? sent = null;
		_fake.OnRequest = async request =>
		{
			if (request.Method == HttpMethod.Put) sent = await request.Content!.ReadAsStringAsync();
		};
		_fake.Extra["PUT /api/profile/Tomas%20Reyes/gallery"] = """
			[{"assetId":"d","fileName":"docks.jpg","url":"/api/wiki-assets/d/docks.jpg","caption":null,"order":0,"isIcon":false,"isBanner":false}]
			""";

		var cut = RenderProfile();
		cut.WaitForAssertion(() => cut.Find(".char-profile-remove-banner"), TimeSpan.FromSeconds(5));
		await cut.Find(".char-profile-remove-banner").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".kit-banner--hue"), TimeSpan.FromSeconds(5));
		await Assert.That(sent).Contains("\"isBanner\":false");
		await Assert.That(cut.FindAll(".char-profile-remove-banner").Count).IsEqualTo(0);
	}
}
