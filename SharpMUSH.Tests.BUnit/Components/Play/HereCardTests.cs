using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Play;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Tests.BUnit.Components.Play;

/// <summary>
/// README §5.6 Here (boards 01, 10): characters as portrait tiles, then objects as square thumbnails.
/// A character with a profile opens the sheet; anything else runs its <c>cmd</c>.
/// </summary>
public class HereCardTests : BunitContext
{
	public HereCardTests()
	{
		Services.AddLocalization();
		Services.AddMudServices();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static RoomOccupant Player(string name, string dbref, string? image = null, string? status = "active", bool profile = true, bool you = false) =>
		new(dbref, name, $"look {dbref}", $"{dbref}:1", "player", null, image is null ? null : new ImageRef(image, name, null, null, null),
			status, 0, profile, you, []);

	private static readonly RoomOccupant Bundle = new("#1142", "Oilcloth bundle", "look #1142", "#1142:1", "thing", null,
		new ImageRef("/api/wiki-assets/o/bundle.jpg", null, null, null, null), null, null, false, false, []);

	private IRenderedComponent<HereCard> RenderHere(IReadOnlyList<RoomOccupant> occupants, Action<RoomOccupant>? onCharacter = null,
		Action<string>? onCommand = null) =>
		Render<HereCard>(p => p.Add(x => x.Occupants, occupants)
			.Add(x => x.OnCharacter, o => onCharacter?.Invoke(o))
			.Add(x => x.OnCommand, c => onCommand?.Invoke(c)));

	[Test]
	public async Task Characters_ComeFirst_ThenObjects_AndTheViewerIsNotListed()
	{
		var cut = RenderHere([Bundle, Player("Ilsa Varn", "#313", you: true), Player("Tomas Reyes", "#312", "/t.jpg"), Player("Wren Halloway", "#314", status: "away")]);
		await Assert.That(cut.Find(".kit-card-title").TextContent).IsEqualTo("Here · 3");
		var labels = cut.FindAll(".here-grid .kit-portrait-label, .here-grid .here-thing-label").Select(e => e.TextContent.Trim()).ToList();
		await Assert.That(labels).IsEquivalentTo(new[] { "Tomas", "Wren · away", "Oilcloth bundle" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task APortrait_ShowsThePicture_AndTheStatusOnlyWhenNotActive()
	{
		var cut = RenderHere([Player("Tomas Reyes", "#312", "/t.jpg"), Player("Wren Halloway", "#314", status: "idle 14m")]);
		var tiles = cut.FindAll(".kit-portrait");
		await Assert.That(tiles[0].QuerySelector("img")!.GetAttribute("src")).IsEqualTo("/t.jpg");
		await Assert.That(tiles[0].QuerySelector(".kit-portrait-label--dim")).IsNull();
		await Assert.That(tiles[1].QuerySelector(".kit-portrait-fallback")!.TextContent).IsEqualTo("WH");
		await Assert.That(tiles[1].QuerySelector(".kit-portrait-label--dim")!.TextContent).IsEqualTo("Wren · idle 14m");
		await Assert.That(tiles[0].GetAttribute("aria-label")).IsEqualTo("Tomas Reyes");
	}

	[Test]
	public async Task ACharacterWithAProfile_OpensTheSheet_OneWithout_RunsItsCommand()
	{
		RoomOccupant? opened = null;
		string? ran = null;
		var cut = RenderHere([Player("Tomas Reyes", "#312"), Player("Pell Marsh", "#320", profile: false)], o => opened = o, c => ran = c);
		await cut.FindAll(".kit-portrait")[0].ClickAsync();
		await Assert.That(opened?.Name).IsEqualTo("Tomas Reyes");
		await Assert.That(ran).IsNull();
		await cut.FindAll(".kit-portrait")[1].ClickAsync();
		await Assert.That(ran).IsEqualTo("look #320");
	}

	[Test]
	public async Task AnObject_IsASquareThumbnail_ThatRunsItsCommand()
	{
		string? ran = null;
		var cut = RenderHere([Bundle], onCommand: c => ran = c);
		var thing = cut.Find("button.here-thing");
		await Assert.That(thing.QuerySelector("img")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/o/bundle.jpg");
		await thing.ClickAsync();
		await Assert.That(ran).IsEqualTo("look #1142");
	}

	[Test]
	public async Task AVersionOneRow_IsAPortraitThatRunsItsCommand()
	{
		string? ran = null;
		var v1 = new RoomOccupant("#5", "Bob", "look #5", null, null, null, null, null, null, false, false, []);
		var cut = RenderHere([v1], onCommand: c => ran = c);
		await cut.Find(".kit-portrait").ClickAsync();
		await Assert.That(ran).IsEqualTo("look #5");
		await Assert.That(cut.Find(".kit-portrait-fallback").TextContent).IsEqualTo("B");
	}

	[Test]
	public async Task AloneInTheRoom_SaysNobodyElseIsHere()
	{
		var cut = RenderHere([Player("Ilsa Varn", "#313", you: true)]);
		await Assert.That(cut.Find(".here-empty").TextContent).IsEqualTo("Nobody else is here.");
	}

	[Test]
	public async Task NobodyHere_SaysSo()
	{
		var cut = RenderHere([]);
		await Assert.That(cut.Find(".kit-card-title").TextContent).IsEqualTo("Here · 0");
		await Assert.That(cut.Find(".here-empty").TextContent).Contains("Connect to see who");
	}
}
