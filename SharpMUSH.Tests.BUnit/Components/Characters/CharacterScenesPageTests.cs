using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Pages;

namespace SharpMUSH.Tests.BUnit.Components.Characters;

/// <summary>
/// /character/{Name}/scenes: every scene of the character's the viewer may see, as scene cards, a page at
/// a time, narrowed by state and by a search that the server applies.
/// </summary>
public class CharacterScenesPageTests : TrackingBunitContext
{
	private readonly CharactersApiFake _fake;

	public CharacterScenesPageTests()
	{
		(_fake, _, _) = CharactersApiFake.Install(this);
		Services.AddSingleton(CharactersApiFake.Anonymous(this));
	}

	private static string Scenes(int from, int count, string status = "finished") =>
		"[" + string.Join(",", Enumerable.Range(from, count).Select(i =>
			"{\"id\":\"" + i + "\",\"status\":\"" + status + "\",\"isPublic\":true,\"isTempRoom\":false,\"startedAt\":1,\"lastActivityAt\":" + (1000 - i)
			+ ",\"poseCount\":3,\"ownerName\":\"Ilsa\",\"starterName\":\"Ilsa\",\"roomName\":\"Room " + i + "\",\"meta\":{\"title\":\"Scene " + i + "\"}}")) + "]";

	private const string FirstPage = "/api/scenes?participant=%23312&count=26";

	private IRenderedComponent<CharacterScenes> RenderPage(string name = "Tomas Reyes")
	{
		var cut = Render<CharacterScenes>(p => p.Add(x => x.Name, name));
		cut.WaitForAssertion(() => cut.Find(".scene-card, .char-scenes-missing, .char-scenes-none, .char-scenes-failed"), TimeSpan.FromSeconds(5));
		return cut;
	}

	[Test]
	public async Task ListsAPage_ThenShowMoreReadsTheNext()
	{
		_fake.Extra[FirstPage] = Scenes(1, 26);
		_fake.Extra["/api/scenes?participant=%23312&count=26&offset=25"] = Scenes(26, 3);

		var cut = RenderPage();

		await Assert.That(cut.Find("h1").TextContent).IsEqualTo("Scenes with Tomas Reyes");
		await Assert.That(cut.Find("a.char-scenes-back").GetAttribute("href")).IsEqualTo("/character/Tomas%20Reyes");
		await Assert.That(cut.FindAll(".scene-card").Count).IsEqualTo(25).Because("the 26th only says there is another page");

		await cut.Find(".char-scenes-more").ClickAsync(new());
		cut.WaitForState(() => cut.FindAll(".scene-card").Count == 28, TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".char-scenes-more").Count).IsEqualTo(0).Because("the last page was short");
		await Assert.That(cut.FindAll(".scene-card-title").Last().TextContent).IsEqualTo("Scene 28");
	}

	[Test]
	public async Task AState_AndASearch_AreAskedOfTheServer()
	{
		_fake.Extra[FirstPage] = Scenes(1, 2);
		_fake.Extra["/api/scenes?participant=%23312&count=26&state=live"] = Scenes(7, 1, "active");
		_fake.Extra["/api/scenes?participant=%23312&count=26&state=live&search=docks"] = "[]";

		var cut = RenderPage();
		await Assert.That(cut.FindAll(".scene-card").Count).IsEqualTo(2);

		await cut.FindAll(".kit-chip").Single(c => c.TextContent == "Live").ClickAsync(new());
		cut.WaitForState(() => cut.FindAll(".scene-card-title") is [{ TextContent: "Scene 7" }], TimeSpan.FromSeconds(5));

		await cut.Find(".char-scenes-search").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = " docks " });
		cut.WaitForAssertion(() => cut.Find(".char-scenes-none"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".char-scenes-none").TextContent).IsEqualTo("No scenes found.");
	}

	[Test]
	public async Task ACharacterNobodyIsCalled_SaysSo()
	{
		var cut = RenderPage("Nobody");
		await Assert.That(cut.Find(".char-scenes-missing").TextContent).Contains("Nobody");
	}

	[Test]
	public async Task AListThatCouldNotBeRead_IsNotAnEmptyOne()
	{
		var cut = RenderPage();
		await Assert.That(cut.FindAll(".char-scenes-failed").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll(".char-scenes-none").Count).IsEqualTo(0);
	}
}
