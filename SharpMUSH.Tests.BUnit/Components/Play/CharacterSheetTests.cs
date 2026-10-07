using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using SharpMUSH.Client.Components.Play;
using SharpMUSH.Client.Models;
using SharpMUSH.Tests.BUnit.Components.Characters;

namespace SharpMUSH.Tests.BUnit.Components.Play;

/// <summary>
/// README §5.7 character sheet drawer (board 08): the portrait with Full image, the name in its colour,
/// the role line, pills (in this scene, idle, dbref), Look · Page · Mail · Profile, the Details and a
/// gallery strip. A modal: focus is trapped, Escape closes, focus goes back.
/// </summary>
public class CharacterSheetTests : TrackingBunitContext
{
	private readonly CharactersApiFake _fake;

	public CharacterSheetTests()
	{
		(_fake, _, _) = CharactersApiFake.Install(this);
		_fake.Extra["/http/profile?objid=%23312%3A1"] = """
			{"character":"Tomas Reyes","objid":"#312:1","dbref":"#312",
			 "fields":{"image":"/api/wiki-assets/t/tomas.jpg","color":"#ffb454","role":"Lamplighter · Guild of Lamplighters"}}
			""";
		_fake.Extra["/api/profile/Tomas%20Reyes/gallery"] = """
			[{"assetId":"a","fileName":"a.jpg","url":"/api/wiki-assets/a/a.jpg","caption":"Tomas","order":0,"isIcon":true,"isBanner":false},
			 {"assetId":"b","fileName":"b.jpg","url":"/api/wiki-assets/b/b.jpg","caption":null,"order":1,"isIcon":false,"isBanner":false},
			 {"assetId":"c","fileName":"c.jpg","url":"/api/wiki-assets/c/c.jpg","caption":"Docks","order":2,"isIcon":false,"isBanner":false},
			 {"assetId":"d","fileName":"d.jpg","url":"/api/wiki-assets/d/d.jpg","caption":null,"order":3,"isIcon":false,"isBanner":false}]
			""";
		_fake.Extra["/api/scenes/42/members"] = """
			[{"sceneId":"42","memberDbref":"#312","memberName":"Tomas Reyes","role":"participant","showAs":"","isCurrent":true,"grantedAt":1},
			 {"sceneId":"42","memberDbref":"#313","memberName":"Ilsa Varn","role":"participant","showAs":"","isCurrent":true,"grantedAt":1}]
			""";
		_fake.Extra["/api/scenes/43/members"] = """
			[{"sceneId":"43","memberDbref":"#313","memberName":"Ilsa Varn","role":"participant","showAs":"","isCurrent":true,"grantedAt":1},
			 {"sceneId":"43","memberDbref":"#312","memberName":"A former #312","role":"participant","showAs":"","isCurrent":false,"grantedAt":1}]
			""";
	}

	private BunitNavigationManager Nav => Services.GetRequiredService<BunitNavigationManager>();

	private static readonly RoomOccupant Tomas = new("#312", "Tomas Reyes", "look #312", "#312:1", "player", "#ffb454",
		new ImageRef("/api/wiki-assets/t/room.jpg", "Tomas", null, null, null), "idle", 90, true, false, [new OccupantAction("Page", "page #312=")]);

	private IRenderedComponent<CharacterSheet> RenderSheet(string? name = "Tomas Reyes", RoomOccupant? occupant = null, string? sceneId = "42",
		Action? onClose = null, Action<string>? onCommand = null, Action<string>? onPage = null)
	{
		var cut = Render<CharacterSheet>(p => p
			.Add(x => x.Name, name)
			.Add(x => x.Occupant, occupant)
			.Add(x => x.SceneId, sceneId)
			.Add(x => x.OnClose, () => onClose?.Invoke())
			.Add(x => x.OnCommand, c => onCommand?.Invoke(c))
			.Add(x => x.OnPage, n => onPage?.Invoke(n))
			.Add(x => x.Details, (RenderFragment<string>)(who => b => b.AddMarkupContent(0, $"<dl id=\"details\">{who}</dl>"))));
		if (name is not null)
		{
			cut.WaitForAssertion(() => cut.Find(".sheet-role"), TimeSpan.FromSeconds(5));
		}
		return cut;
	}

	[Test]
	public async Task Closed_RendersNothing()
		=> await Assert.That(RenderSheet(name: null).Markup.Trim()).IsEmpty();

	[Test]
	public async Task TheSheet_IsAModal_NamedForTheCharacter_WithFocusTrapped()
	{
		var cut = RenderSheet(occupant: Tomas);
		var dialog = cut.Find("[role='dialog']");
		await Assert.That(dialog.GetAttribute("aria-modal")).IsEqualTo("true");
		await Assert.That(dialog.GetAttribute("aria-label")).IsEqualTo("Character sheet: Tomas Reyes");
		await Assert.That(cut.FindComponents<MudFocusTrap>().Count).IsEqualTo(1);
		JSInterop.VerifyInvoke("sharpmushLayout.rememberFocus");
		// MudOverlay centres a zero-size content box; without this the sheet collapsed to nothing.
		await Assert.That(cut.Find(".mud-overlay").GetAttribute("style")).Contains("justify-content: flex-end");
	}

	[Test]
	public async Task ThePortrait_TheName_TheRole_AndThePills()
	{
		var cut = RenderSheet(occupant: Tomas);
		await Assert.That(cut.Find("img.sheet-portrait").GetAttribute("src")).IsEqualTo("/api/wiki-assets/t/room.jpg")
			.Because("the room row's picture is what the viewer sees on the tile they clicked");
		await Assert.That(cut.Find(".sheet-name").TextContent).IsEqualTo("Tomas Reyes");
		await Assert.That(cut.Find(".sheet-name").GetAttribute("style")).Contains("#ffb454");
		await Assert.That(cut.Find(".sheet-role").TextContent).IsEqualTo("Lamplighter · Guild of Lamplighters");
		var pills = cut.FindAll(".sheet-pills .kit-pill").Select(p => p.TextContent.Trim()).ToList();
		await Assert.That(pills).IsEquivalentTo(new[] { "In this scene", "Idle 1m", "#312" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task SomeoneInTheRoom_ButNotInItsScene_HasNoScenePill()
	{
		var cut = RenderSheet(occupant: Tomas, sceneId: "43");
		cut.WaitForAssertion(() => cut.Find(".sheet-pills .kit-pill"), TimeSpan.FromSeconds(5));
		var pills = cut.FindAll(".sheet-pills .kit-pill").Select(p => p.TextContent.Trim()).ToList();
		await Assert.That(pills).IsEquivalentTo(new[] { "Idle 1m", "#312" }, TUnit.Assertions.Enums.CollectionOrdering.Matching)
			.Because("a passer-by in the room is not in its scene; #312's only membership belongs to a former holder");
	}

	[Test]
	public async Task SomeoneNotInTheRoom_UsesTheProfilesPicture_AndHasNoScenePill()
	{
		var cut = RenderSheet(occupant: null);
		await Assert.That(cut.Find("img.sheet-portrait").GetAttribute("src")).IsEqualTo("/api/wiki-assets/t/tomas.jpg");
		var pills = cut.FindAll(".sheet-pills .kit-pill").Select(p => p.TextContent.Trim()).ToList();
		await Assert.That(pills).IsEquivalentTo(new[] { "#312" });
	}

	[Test]
	public async Task LookPageMailProfile()
	{
		string? ran = null;
		string? paged = null;
		var cut = RenderSheet(occupant: Tomas, onCommand: c => ran = c, onPage: n => paged = n);
		var actions = cut.FindAll(".sheet-actions > *");
		await Assert.That(actions.Select(a => a.TextContent.Trim()).ToList())
			.IsEquivalentTo(new[] { "Look", "Page", "Mail", "Profile" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await actions[0].ClickAsync();
		await Assert.That(ran).IsEqualTo("look #312");
		await cut.FindAll(".sheet-actions > *")[1].ClickAsync();
		await Assert.That(paged).IsEqualTo("Tomas Reyes");
		await Assert.That(cut.FindAll(".sheet-actions > *")[2].GetAttribute("href")).IsEqualTo("/mail/compose?to=Tomas%20Reyes");
		await Assert.That(cut.FindAll(".sheet-actions > *")[3].GetAttribute("href")).IsEqualTo("/character/Tomas%20Reyes");
	}

	[Test]
	public async Task TheDetails_AreThePagesToProvide()
	{
		var cut = RenderSheet(occupant: Tomas);
		await Assert.That(cut.Find("#details").TextContent).IsEqualTo("Tomas Reyes");
	}

	[Test]
	public async Task TheGalleryStrip_ShowsThree_AndViewAllLinksTheProfile()
	{
		var cut = RenderSheet(occupant: Tomas);
		cut.WaitForAssertion(() => cut.Find(".sheet-gallery img"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".sheet-gallery button").Count).IsEqualTo(3);
		await Assert.That(cut.Find(".sheet-gallery-head").TextContent).Contains("4");
		await Assert.That(cut.Find("a.sheet-gallery-all").GetAttribute("href")).IsEqualTo("/character/Tomas%20Reyes");
	}

	[Test]
	public async Task FullImage_OpensTheViewer_OnThePortrait()
	{
		var cut = RenderSheet(occupant: null);
		cut.WaitForAssertion(() => cut.Find(".sheet-gallery img"), TimeSpan.FromSeconds(5));
		await cut.Find("button.sheet-full").ClickAsync();
		await Assert.That(cut.Find(".kit-viewer img").GetAttribute("src")).IsEqualTo("/api/wiki-assets/t/tomas.jpg");
	}

	[Test]
	public async Task AColourThatIsNotHex_IsIgnored()
	{
		var cut = RenderSheet(occupant: Tomas with { Color = "#zz;x:1" });
		await Assert.That(cut.Find(".sheet-name").GetAttribute("style")).IsNull();
	}

	[Test]
	public async Task AfterFullImage_TheGalleryStripStillOpensItsOwnPicture()
	{
		var cut = RenderSheet(occupant: Tomas);
		cut.WaitForAssertion(() => cut.Find(".sheet-gallery img"), TimeSpan.FromSeconds(5));
		await cut.Find("button.sheet-full").ClickAsync();
		await Assert.That(cut.Find(".kit-viewer img").GetAttribute("src")).IsEqualTo("/api/wiki-assets/t/room.jpg");
		await cut.Find(".kit-viewer").KeyDownAsync("Escape");
		await cut.FindAll(".sheet-gallery button")[1].ClickAsync();
		await Assert.That(cut.Find(".kit-viewer img").GetAttribute("src")).IsEqualTo("/api/wiki-assets/b/b.jpg");
	}

	[Test]
	public async Task EscapeAndClose_Close()
	{
		var closed = 0;
		var cut = RenderSheet(occupant: Tomas, onClose: () => closed++);
		await cut.Find("[role='dialog']").KeyDownAsync("Escape");
		await Assert.That(closed).IsEqualTo(1);
		await cut.Find("button.sheet-close").ClickAsync();
		await Assert.That(closed).IsEqualTo(2);
		JSInterop.VerifyInvoke("sharpmushLayout.restoreFocus", calledTimes: 2);
	}

	[Test]
	public async Task AnUnreadableProfile_StillShowsTheName_AndTheRoomsPicture()
	{
		_fake.Extra.Remove("/http/profile?objid=%23312%3A1");
		var cut = Render<CharacterSheet>(p => p.Add(x => x.Name, "Tomas Reyes").Add(x => x.Occupant, Tomas));
		cut.WaitForAssertion(() => cut.Find(".sheet-name"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".sheet-name").TextContent).IsEqualTo("Tomas Reyes");
		await Assert.That(cut.Find("img.sheet-portrait").GetAttribute("src")).IsEqualTo("/api/wiki-assets/t/room.jpg");
		await Assert.That(Nav.Uri).DoesNotContain("character");
	}

	[Test]
	public async Task TheProfileAndTheGallery_AreAskedForTogether()
	{
		// The profile's answer is held until the gallery has been asked for: loaded one after the
		// other, the gallery request never comes while the profile is pending.
		var galleryAsked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var galleryAskedDuringProfile = false;
		_fake.OnRequest = async request =>
		{
			var path = request.RequestUri!.PathAndQuery;
			if (path.StartsWith("/api/profile/Tomas%20Reyes/gallery", StringComparison.Ordinal))
			{
				galleryAsked.TrySetResult();
			}
			else if (path.StartsWith("/http/profile", StringComparison.Ordinal))
			{
				galleryAskedDuringProfile = await Task.WhenAny(galleryAsked.Task, Task.Delay(TimeSpan.FromSeconds(2))) == galleryAsked.Task;
			}
		};

		var cut = RenderSheet(occupant: Tomas);
		cut.WaitForAssertion(() => cut.Find(".sheet-gallery-item"), TimeSpan.FromSeconds(5));

		await Assert.That(galleryAskedDuringProfile).IsTrue();
	}
}
