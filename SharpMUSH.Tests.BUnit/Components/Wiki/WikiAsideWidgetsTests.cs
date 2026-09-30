using System.Net;
using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

/// <summary>Answers the scene list the way the REST API does.</summary>
file sealed class SceneListHandler(string body) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
		Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
}

/// <summary>
/// The two widgets in the wiki home aside (board 21) as D1 aside cards: "Recently changed" rows
/// with a thumbnail, the editor and when; "Live now · N" with a tile per live scene.
/// </summary>
public class WikiAsideWidgetsTests : TrackingBunitContext
{
	public WikiAsideWidgetsTests()
	{
		WikiApiFake.Install(this);
	}

	[Test]
	public async Task RecentWikiActivity_IsAnAsideCardOfRows_WithThumbnailEditorAndWhen()
	{
		var cut = Render<RecentWikiActivityWidget>();
		cut.WaitForAssertion(() => cut.Find(".kit-card--aside .kit-row"), TimeSpan.FromSeconds(5));
		var rows = cut.FindAll(".kit-card--aside a.kit-row");
		await Assert.That(rows.Count).IsEqualTo(4);
		await Assert.That(rows[1].QuerySelector(".kit-row-label")!.TextContent).IsEqualTo("Harbour Ward");
		await Assert.That(rows[1].QuerySelector(".kit-row-sub")!.TextContent).Contains("Wren");
		await Assert.That(rows[1].QuerySelector("img.kit-row-img")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/b/harbour.jpg");
		await Assert.That(rows[0].QuerySelector(".kit-row-fallback svg")).IsNotNull()
			.Because("the newest page (Loose Notes) has no image, so its row leads with the document icon");
		await Assert.That(cut.Find(".kit-card-title").TextContent).IsNotEmpty();
	}

	[Test]
	public async Task ActiveScene_ListsLiveScenesAsTiles_WithTheCountInTheTitle()
	{
		const string scenes = """
		[
		  {"id":"42","status":"active","isPublic":true,"isTempRoom":false,"poseCount":12,"ownerDbref":"#1","ownerName":"Ilsa","starterDbref":"#1","starterName":"Ilsa","roomDbref":"#1201","roomName":"Lower Docks","meta":{"title":"Salt Market at Dusk"}},
		  {"id":"43","status":"active","isPublic":true,"isTempRoom":false,"poseCount":3,"ownerDbref":"#2","ownerName":"Wren","starterDbref":"#2","starterName":"Wren","roomDbref":"#1210","roomName":"Harbour Row","meta":{}}
		]
		""";
		var client = Track(new HttpClient(new SceneListHandler(scenes)) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		Services.AddSingleton(new SceneService(factory));

		var cut = Render<ActiveSceneWidget>();
		cut.WaitForAssertion(() => cut.Find(".kit-card--aside .kit-tile"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".kit-card-title").TextContent).Contains("2");
		var tiles = cut.FindAll("a.kit-tile");
		await Assert.That(tiles.Count).IsEqualTo(2);
		await Assert.That(tiles[0].QuerySelector(".kit-tile-label")!.TextContent).IsEqualTo("Salt Market at Dusk");
		await Assert.That(tiles[0].GetAttribute("href")).IsEqualTo("/scenes/42/live");
		await Assert.That(tiles[0].QuerySelector(".kit-tile-count")!.TextContent).IsEqualTo("12").Because("board 21 shows the pose count at the tile's edge");
		await Assert.That(tiles[1].QuerySelector(".kit-tile-label")!.TextContent).Contains("Harbour Row");
	}

	[Test]
	public async Task ActiveScene_WithNoScenes_OffersTheArchive()
	{
		var client = Track(new HttpClient(new SceneListHandler("[]")) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		Services.AddSingleton(new SceneService(factory));

		var cut = Render<ActiveSceneWidget>();
		cut.WaitForAssertion(() => cut.Find(".kit-card--aside"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find("a[href='/scenes']")).IsNotNull();
		await Assert.That(cut.FindAll(".kit-tile").Count).IsEqualTo(0);
	}
}
