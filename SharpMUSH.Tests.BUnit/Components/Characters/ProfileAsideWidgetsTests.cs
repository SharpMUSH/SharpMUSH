using Bunit;
using Microsoft.AspNetCore.Components;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models.Widgets;

namespace SharpMUSH.Tests.BUnit.Components.Characters;

/// <summary>
/// The profile aside (board 25): Gallery · N with the portrait large and two more small, View all
/// opening the viewer (where the owner's actions live); Recent scenes; Often plays with.
/// </summary>
public class ProfileAsideWidgetsTests : TrackingBunitContext
{
	private readonly CharactersApiFake _fake;

	public ProfileAsideWidgetsTests()
	{
		(_fake, _, _) = CharactersApiFake.Install(this);
		_fake.Extra["/api/profile/Tomas%20Reyes/gallery"] = """
			[{"assetId":"a","fileName":"a.jpg","url":"/api/wiki-assets/a/a.jpg","caption":"Tomas","order":0,"isIcon":true,"isBanner":false},
			 {"assetId":"b","fileName":"b.jpg","url":"/api/wiki-assets/b/b.jpg","caption":null,"order":1,"isIcon":false,"isBanner":true},
			 {"assetId":"c","fileName":"c.jpg","url":"/api/wiki-assets/c/c.jpg","caption":"Docks","order":2,"isIcon":false,"isBanner":false},
			 {"assetId":"d","fileName":"d.jpg","url":"/api/wiki-assets/d/d.jpg","caption":null,"order":3,"isIcon":false,"isBanner":false}]
			""";
	}

	private IRenderedComponent<T> InProfile<T>(bool canEdit = false) where T : IComponent
	{
		var cut = Render<WikiBodyBiographyTests.CascadingWrapper>(p => p.AddChildContent<T>()
			.Add(x => x.Context, new ProfilePageContext("Tomas Reyes", canEdit)));
		return cut.FindComponent<T>();
	}

	[Test]
	public async Task Gallery_IsAnAsideCard_WithThePortraitLarge_AndTwoSmall()
	{
		var cut = InProfile<CharacterGalleryWidget>();
		cut.WaitForAssertion(() => cut.Find(".gallery-hero img"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".kit-card--aside .kit-card-title").TextContent).IsEqualTo("Gallery · 4");
		await Assert.That(cut.Find(".gallery-hero img").GetAttribute("src")).IsEqualTo("/api/wiki-assets/a/a.jpg");
		await Assert.That(cut.Find(".gallery-hero .gallery-star")).IsNotNull();
		var small = cut.FindAll(".gallery-small img");
		await Assert.That(small.Count).IsEqualTo(2).Because("the board shows the portrait and two more; View all has the rest");
		await Assert.That(cut.FindAll("button.gallery-view-all").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll(".gallery-upload").Count).IsEqualTo(0).Because("a visitor cannot add images");
	}

	[Test]
	public async Task ViewAll_OpensTheViewer_WithOwnerActionsOnlyForTheOwner()
	{
		var visitor = InProfile<CharacterGalleryWidget>();
		visitor.WaitForAssertion(() => visitor.Find("button.gallery-view-all"), TimeSpan.FromSeconds(5));
		await visitor.Find("button.gallery-view-all").ClickAsync();
		await Assert.That(visitor.Find(".kit-viewer-count").TextContent).Contains("4");
		await Assert.That(visitor.FindAll(".kit-viewer-actions button").Count).IsEqualTo(0);
	}

	[Test]
	public async Task TheOwner_CanMakeAnImageTheBanner()
	{
		string? putBody = null;
		_fake.OnRequest = async request =>
		{
			if (request.Method == HttpMethod.Put) putBody = await request.Content!.ReadAsStringAsync();
		};
		_fake.Extra["PUT /api/profile/Tomas%20Reyes/gallery"] = "[]";

		var cut = InProfile<CharacterGalleryWidget>(canEdit: true);
		cut.WaitForAssertion(() => cut.Find("button.gallery-view-all"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".gallery-upload button.gallery-add").Count).IsEqualTo(1).Because("a kit capsule, not MudBlazor's default upload button");
		await cut.Find("button.gallery-view-all").ClickAsync();
		await cut.Find("button.kit-viewer-next").ClickAsync();
		await cut.Find("button.kit-viewer-next").ClickAsync();
		await cut.Find(".kit-viewer-actions button.gallery-make-banner").ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (putBody is null) throw new InvalidOperationException("no PUT yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(putBody!).Contains("\"assetId\":\"c\"");
		using var doc = System.Text.Json.JsonDocument.Parse(putBody!);
		var banners = doc.RootElement.EnumerateArray().Where(e => e.GetProperty("isBanner").GetBoolean()).Select(e => e.GetProperty("assetId").GetString()).ToList();
		await Assert.That(banners).IsEquivalentTo(new[] { "c" });
	}

	[Test]
	public async Task RecentScenes_ListTheCharactersScenes_LiveFirstAsLiveNow()
	{
		_fake.Extra["/api/scenes?participant=%23312&count=3"] = """
			[{"id":"42","status":"active","isPublic":true,"isTempRoom":false,"startedAt":1,"lastActivityAt":2,"poseCount":12,
			  "ownerName":"Ilsa","starterName":"Ilsa","roomName":"Lower Docks","meta":{"title":"Salt Market at Dusk"}},
			 {"id":"41","status":"finished","isPublic":true,"isTempRoom":false,"startedAt":1,"lastActivityAt":1,"poseCount":40,
			  "ownerName":"Ilsa","starterName":"Ilsa","roomName":"Ferry Steps","meta":{}}]
			""";
		var cut = InProfile<RecentScenesWidget>();
		cut.WaitForAssertion(() => cut.Find(".kit-card--aside a.kit-row"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".kit-card-title").TextContent).IsEqualTo("Recent scenes");
		var rows = cut.FindAll("a.kit-row");
		await Assert.That(rows.Count).IsEqualTo(2);
		await Assert.That(rows[0].GetAttribute("href")).IsEqualTo("/scenes/42/live");
		await Assert.That(rows[0].QuerySelector(".kit-row-label")!.TextContent).IsEqualTo("Salt Market at Dusk");
		await Assert.That(rows[0].QuerySelector(".kit-row-sub")!.TextContent).Contains("live now");
		await Assert.That(rows[1].GetAttribute("href")).IsEqualTo("/scenes/41");
		await Assert.That(rows[1].QuerySelector(".kit-row-label")!.TextContent).IsEqualTo("Ferry Steps");
	}

	[Test]
	public async Task RecentScenes_SaysWhenItCouldNotAsk_AndWhenThereAreNone()
	{
		var failed = InProfile<RecentScenesWidget>();
		failed.WaitForAssertion(() => failed.Find(".recent-scenes-empty"), TimeSpan.FromSeconds(5));
		await Assert.That(failed.Find(".recent-scenes-empty").TextContent).IsNotEqualTo("No scenes yet.")
			.Because("a request that failed is not a character with no scenes");
	}

	[Test]
	public async Task RecentScenes_WithNone_SaysSo()
	{
		_fake.Extra["/api/scenes?participant=%23312&count=3"] = "[]";
		var cut = InProfile<RecentScenesWidget>();
		cut.WaitForAssertion(() => cut.Find(".recent-scenes-empty"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".recent-scenes-empty").TextContent).IsEqualTo("No scenes yet.");
	}

	[Test]
	public async Task OftenPlaysWith_ShowsPartnersAsPortraits_FromTheDirectory()
	{
		_fake.Extra["/api/scenes/partners?participant=%23312&count=4"] = """
			[{"dbref":"#313","name":"Ilsa Varn","scenes":4},{"dbref":"#314","name":"Wren Halloway","scenes":2}]
			""";
		var cut = InProfile<OftenPlaysWithWidget>();
		cut.WaitForAssertion(() => cut.Find(".kit-card--aside a.kit-portrait"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".kit-card-title").TextContent).IsEqualTo("Often plays with");
		var tiles = cut.FindAll("a.kit-portrait");
		await Assert.That(tiles.Count).IsEqualTo(2);
		await Assert.That(tiles[0].GetAttribute("href")).IsEqualTo("/character/Ilsa%20Varn");
		await Assert.That(tiles[0].QuerySelector("img")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/i/ilsa.jpg");
		await Assert.That(tiles[1].QuerySelector(".kit-portrait-label")!.TextContent).IsEqualTo("Wren Halloway");
	}
}
