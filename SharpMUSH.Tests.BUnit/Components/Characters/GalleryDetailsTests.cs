using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Client.Pages;

namespace SharpMUSH.Tests.BUnit.Components.Characters;

/// <summary>
/// What the gallery adds to an image beyond the picture: a content warning that covers it until the
/// viewer chooses to see it, a caption, tags the gallery page filters by, a place in the order the owner
/// moves, and a report a signed-in visitor sends to staff when the game takes them.
/// </summary>
public class GalleryDetailsTests : TrackingBunitContext
{
	private readonly CharactersApiFake _fake;
	private readonly BunitAuthorizationContext _auth;

	private const string GalleryPath = "/api/profile/Tomas%20Reyes/gallery";

	public GalleryDetailsTests()
	{
		(_fake, _, _auth) = CharactersApiFake.Install(this);
		_fake.Extra[GalleryPath] = """
			[{"assetId":"a","fileName":"a.jpg","url":"/api/wiki-assets/a/a.jpg","caption":"Tomas","order":0,"isIcon":true,"isBanner":false,"tags":["portrait"]},
			 {"assetId":"b","fileName":"b.jpg","url":"/api/wiki-assets/b/b.jpg","caption":"After the fight","order":1,"isIcon":false,"isBanner":false,"tags":["docks"],"warning":"blood"},
			 {"assetId":"c","fileName":"c.jpg","url":"/api/wiki-assets/c/c.jpg","caption":"Docks","order":2,"isIcon":false,"isBanner":false,"tags":["docks","night"]}]
			""";
	}

	private IRenderedComponent<CharacterGalleryWidget> Widget(bool canEdit = false)
	{
		var cut = Render<WikiBodyBiographyTests.CascadingWrapper>(p => p.AddChildContent<CharacterGalleryWidget>()
			.Add(x => x.Context, new ProfilePageContext("Tomas Reyes", canEdit)));
		var widget = cut.FindComponent<CharacterGalleryWidget>();
		widget.WaitForAssertion(() => widget.Find("button.gallery-hero img"), TimeSpan.FromSeconds(5));
		return widget;
	}

	/// <summary>Captures the body of the first request with <paramref name="method"/>.</summary>
	private Func<string?> Capture(HttpMethod method)
	{
		string? body = null;
		_fake.OnRequest = async request =>
		{
			if (request.Method == method && body is null) body = request.Content is null ? "" : await request.Content.ReadAsStringAsync();
		};
		return () => body;
	}

	private static void WaitFor(IRenderedComponent<CharacterGalleryWidget> cut, Func<string?> body) =>
		cut.WaitForAssertion(() =>
		{
			if (body() is null) throw new InvalidOperationException("no request yet");
		}, TimeSpan.FromSeconds(5));

	[Test]
	public async Task AWarnedImage_IsCoveredOnItsTile_AndNotLoaded()
	{
		var cut = Widget();

		var tile = cut.FindAll("button.gallery-small-tile")[0];
		await Assert.That(tile.QuerySelector("img")).IsNull().Because("the picture is not fetched until someone chooses to see it");
		await Assert.That(tile.QuerySelector(".gallery-cover")!.TextContent).Contains("blood");
		await Assert.That(tile.GetAttribute("aria-label")).IsEqualTo("Content warning: blood");
	}

	[Test]
	public async Task TheViewer_CoversAWarnedImage_UntilShowImage()
	{
		var cut = Widget();
		await cut.FindAll("button.gallery-small-tile")[0].ClickAsync();

		await Assert.That(cut.FindAll(".kit-viewer-img").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".kit-viewer-cover").TextContent).Contains("blood");
		await cut.Find("button.kit-viewer-reveal").ClickAsync();

		await Assert.That(cut.Find(".kit-viewer-img").GetAttribute("src")).IsEqualTo("/api/wiki-assets/b/b.jpg");
		await Assert.That(cut.Find(".kit-viewer-tags").TextContent).Contains("docks");
	}

	[Test]
	public async Task AWarnedImage_OffersTheOwnerNeitherPortraitNorBanner()
	{
		var cut = Widget(canEdit: true);
		await cut.FindAll("button.gallery-small-tile")[0].ClickAsync();

		await Assert.That(cut.FindAll(".kit-viewer-actions .gallery-make-icon").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".kit-viewer-actions .gallery-make-banner").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".kit-viewer-actions .gallery-edit").Count).IsEqualTo(1);
	}

	[Test]
	public async Task TheOwner_EditsCaptionTagsAndWarning()
	{
		var put = Capture(HttpMethod.Put);
		_fake.Extra[$"PUT {GalleryPath}"] = "[]";
		var cut = Widget(canEdit: true);
		await cut.FindAll("button.gallery-small-tile")[1].ClickAsync();
		await cut.Find(".kit-viewer-actions button.gallery-edit").ClickAsync();

		await cut.Find("input.gallery-caption-input").ChangeAsync(new() { Value = "  The ferry  " });
		await cut.Find("input.gallery-tags-input").ChangeAsync(new() { Value = "Night, ferry, night" });
		await cut.Find("input.gallery-warning-input").ChangeAsync(new() { Value = "spiders" });
		await cut.Find("form.gallery-details").SubmitAsync();

		WaitFor(cut, put);
		using var doc = JsonDocument.Parse(put()!);
		var c = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("assetId").GetString() == "c");
		await Assert.That(c.GetProperty("caption").GetString()).IsEqualTo("The ferry");
		await Assert.That(c.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToList()).IsEquivalentTo(new[] { "night", "ferry" });
		await Assert.That(c.GetProperty("warning").GetString()).IsEqualTo("spiders");
	}

	[Test]
	public async Task TheOwner_MovesAnImageLater()
	{
		var put = Capture(HttpMethod.Put);
		_fake.Extra[$"PUT {GalleryPath}"] = "[]";
		var cut = Widget(canEdit: true);
		await cut.Find("button.gallery-hero").ClickAsync();
		await cut.Find(".kit-viewer-actions button.gallery-move-later").ClickAsync();

		WaitFor(cut, put);
		using var doc = JsonDocument.Parse(put()!);
		var order = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("assetId").GetString()).ToList();
		await Assert.That(order).IsEquivalentTo(new[] { "b", "a", "c" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task ASignedInVisitor_ReportsAnImage_WhenTheGameTakesReports()
	{
		_auth.SetAuthorized("visitor");
		_fake.Extra["/api/gallery/reports"] = """{"taken":true}""";
		_fake.Extra[$"POST {GalleryPath}/c/report"] = "{}";
		var post = Capture(HttpMethod.Post);
		var cut = Widget();
		await cut.FindAll("button.gallery-small-tile")[1].ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".kit-viewer-actions button.gallery-report"), TimeSpan.FromSeconds(5));
		await cut.Find(".kit-viewer-actions button.gallery-report").ClickAsync();

		await cut.Find("textarea.gallery-report-reason").InputAsync(new() { Value = "Not my character" });
		await cut.Find("form.gallery-details").SubmitAsync();

		WaitFor(cut, post);
		await Assert.That(post()!).Contains("\"reason\":\"Not my character\"");
	}

	[Test]
	public async Task NoReportButton_WhenTheGameTakesNoReports()
	{
		_auth.SetAuthorized("visitor");
		_fake.Extra["/api/gallery/reports"] = """{"taken":false}""";
		var cut = Widget();
		await cut.FindAll("button.gallery-small-tile")[1].ClickAsync();
		await Task.Delay(50);

		await Assert.That(cut.FindAll(".kit-viewer-actions button").Count).IsEqualTo(0);
	}

	[Test]
	public async Task TheGalleryPage_FiltersByTag()
	{
		Services.AddSingleton(CharactersApiFake.Anonymous(this));
		var cut = Render<CharacterGallery>(p => p.Add(x => x.Name, "Tomas Reyes"));
		cut.WaitForAssertion(() => cut.Find(".char-gallery-tile"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".char-gallery-tile").Count).IsEqualTo(3);

		var night = cut.FindAll("button").First(b => b.TextContent.Trim() == "night");
		await night.ClickAsync();

		var tiles = cut.FindAll(".char-gallery-tile");
		await Assert.That(tiles.Count).IsEqualTo(1);
		await Assert.That(tiles[0].QuerySelector("img")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/c/c.jpg");
	}
}
