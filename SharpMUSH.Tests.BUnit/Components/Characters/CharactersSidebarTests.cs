using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Characters;
using SharpMUSH.Client.Services;
using AccountCharacter = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Components.Characters;

/// <summary>
/// The Characters section sidebar (board 25): "Characters · ● N online · N characters", a search,
/// Online now, Your characters with Create a character, and Browse.
/// </summary>
public class CharactersSidebarTests : TrackingBunitContext
{
	public CharactersSidebarTests() => CharactersApiFake.Install(this);

	/// <summary>Resolved on use: resolving a service seals the container, and each test registers its own account first.</summary>
	private BunitNavigationManager Nav => Services.GetRequiredService<BunitNavigationManager>();

	private IRenderedComponent<CharactersSidebar> RenderAt(string path, AccountAuthService auth)
	{
		Services.AddSingleton(auth);
		Nav.NavigateTo(path);
		var cut = Render<CharactersSidebar>();
		cut.WaitForAssertion(() => cut.Find(".char-side-online a.kit-row"), TimeSpan.FromSeconds(5));
		return cut;
	}

	[Test]
	public async Task HeaderCountsOnlineAndAll()
	{
		var cut = RenderAt("/characters", CharactersApiFake.Anonymous(this));
		var sub = cut.Find(".kit-side-sub").TextContent;
		await Assert.That(sub).Contains("2 online");
		await Assert.That(sub).Contains("5 characters");
		await Assert.That(cut.Find(".kit-side-sub .char-side-dot")).IsNotNull();
	}

	[Test]
	public async Task OnlineNow_LinksEachProfile_WithItsImage_AndMarksTheCurrentOne()
	{
		var cut = RenderAt("/character/Tomas%20Reyes", CharactersApiFake.Anonymous(this));
		var rows = cut.FindAll(".char-side-online a.kit-row");
		await Assert.That(rows.Count).IsEqualTo(2);
		await Assert.That(rows[0].GetAttribute("href")).IsEqualTo("/character/Tomas%20Reyes");
		await Assert.That(rows[0].QuerySelector("img")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/t/tomas.jpg")
			.Because("the online list is names only; the image comes from the directory row");
		await Assert.That(rows[0].GetAttribute("aria-current")).IsEqualTo("page");
		await Assert.That(rows[1].GetAttribute("aria-current")).IsNull();
		await Assert.That(cut.Find("a.char-side-all-online").GetAttribute("href")).IsEqualTo("/characters?online=1");
	}

	[Test]
	public async Task YourCharacters_AreNotOffered_ToAnAnonymousVisitor()
	{
		var anonymous = RenderAt("/characters", CharactersApiFake.Anonymous(this));
		await Assert.That(anonymous.FindAll(".char-side-mine").Count).IsEqualTo(0);
	}

	[Test]
	public async Task YourCharacters_WhenSignedIn_WithYouOnTheActingOne_AndCreate()
	{
		var signedIn = await CharactersApiFake.SignedInAsync(this,
			new AccountCharacter(313, 1, "Ilsa Varn", "PLAYER", IsActing: true),
			new AccountCharacter(317, 1, "Pell Marsh", "PLAYER"));
		var cut = RenderAt("/characters", signedIn);
		cut.WaitForAssertion(() => cut.Find(".char-side-mine a.kit-row"), TimeSpan.FromSeconds(5));
		var mine = cut.FindAll(".char-side-mine a.kit-row");
		await Assert.That(mine.Count).IsEqualTo(2);
		await Assert.That(mine[0].QuerySelector(".kit-row-trail")!.TextContent).IsEqualTo("you");
		await Assert.That(cut.Find(".char-side-mine a.char-side-create").GetAttribute("href")).IsEqualTo("/characters/new");
	}

	[Test]
	public async Task Browse_AllAndNewest_WithCounts()
	{
		var cut = RenderAt("/characters", CharactersApiFake.Anonymous(this));
		var rows = cut.FindAll(".char-side-browse a.kit-row");
		await Assert.That(rows[0].GetAttribute("href")).IsEqualTo("/characters");
		await Assert.That(rows[0].QuerySelector(".kit-row-count")!.TextContent).IsEqualTo("5");
		await Assert.That(rows[1].GetAttribute("href")).IsEqualTo("/characters?new=1");
		await Assert.That(rows[1].QuerySelector(".kit-row-count")!.TextContent).IsEqualTo("2")
			.Because("two characters were created in the last fortnight");
	}

	[Test]
	public async Task Search_GoesToTheDirectoryWithTheQuery()
	{
		var cut = RenderAt("/characters", CharactersApiFake.Anonymous(this));
		// Every load has landed (the browse counts are the last to render) before typing, so no later
		// render can land between the keystroke and the submit.
		cut.WaitForAssertion(() => cut.Find(".char-side-browse .kit-row-count"), TimeSpan.FromSeconds(5));
		await cut.Find(".kit-side-search input").InputAsync("wren");
		await cut.Find(".kit-side-search").SubmitAsync();
		cut.WaitForAssertion(() =>
		{
			if (!Nav.Uri.EndsWith("/characters?q=wren", StringComparison.Ordinal))
				throw new InvalidOperationException($"still at {Nav.Uri}");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(Nav.Uri).EndsWith("/characters?q=wren");
	}

	[Test]
	public async Task Collapsed_KeepsTheOnlineAvatarsOnly()
	{
		Services.AddSingleton(CharactersApiFake.Anonymous(this));
		Nav.NavigateTo("/characters");
		var cut = Render<CharactersSidebar>(p => p.Add(x => x.Collapsed, true));
		cut.WaitForAssertion(() => cut.Find(".char-side-online .kit-row--collapsed"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".kit-side-head").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".char-side-browse").Count).IsEqualTo(0);
	}
}
