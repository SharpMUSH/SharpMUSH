using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Play;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Components.Play;

/// <summary>
/// README §5.6 Exits (boards 01, 10, 11): image tiles in every state the payload can send — open,
/// occupied, locked with its hint, closed, leaves-the-scene (asks first), no image — a keycap for the
/// first alias, alias keys that go, and a preview on hover or focus.
/// </summary>
public class ExitsCardTests : BunitContext
{
	public ExitsCardTests()
	{
		Services.AddLocalization();
		Services.AddMudServices();
		Services.AddSingleton<ScreenReaderMode>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static ImageRef Img(string url) => new(url, null, null, null, null);

	private static readonly RoomExit Row = new("#1210", "Harbour Row", "goto #1210", "#1210:1", ["n", "north"], ExitState.Open, null, null,
		new ExitDestination("Harbour Row", "Harbour Ward", Img("/r/row.jpg"), "A lamplit street of chandlers.", 2));

	private static readonly RoomExit Customs = new("#1211", "Customs House", "goto #1211", "#1211:1", ["w"], ExitState.Locked, "Closed after dusk", null, null);

	private static readonly RoomExit Ferry = new("#1212", "Ferry Steps", "goto #1212", "#1212:1", ["e"], ExitState.Open, null, "This leaves the scene.",
		new ExitDestination("Ferry Steps", "Harbour Ward", Img("/r/ferry.jpg"), null, 0));

	private static readonly RoomExit Door = new("#1213", "Cellar", "goto #1213", "#1213:1", ["d"], ExitState.Closed, null, null, null);

	private static readonly RoomExit Out = new("#1214", "Out", "goto #1214", "#1214:1", ["out", "o"], ExitState.Open, null, null,
		new ExitDestination("Salt Market", null, null, null, null));

	private IRenderedComponent<ExitsCard> RenderExits(IReadOnlyList<RoomExit> exits, Action<string>? onCommand = null, bool rows = false) =>
		Render<ExitsCard>(p => p.Add(x => x.Exits, exits).Add(x => x.Rows, rows).Add(x => x.OnCommand, c => onCommand?.Invoke(c)));

	private static AngleSharp.Dom.IElement Tile(IRenderedComponent<ExitsCard> cut, string name) =>
		cut.FindAll(".exit").Single(e => e.QuerySelector(".exit-name")!.TextContent == name);

	[Test]
	public async Task AnOpenExit_IsItsDestinationsPicture_ItsName_AndAKeycap_AndGoes()
	{
		string? ran = null;
		var cut = RenderExits([Row, Customs, Ferry], c => ran = c);
		await Assert.That(cut.Find(".kit-card-title").TextContent).IsEqualTo("Exits · 3");
		var tile = Tile(cut, "Harbour Row");
		await Assert.That(tile.QuerySelector("img")!.GetAttribute("src")).IsEqualTo("/r/row.jpg");
		await Assert.That(tile.QuerySelector("kbd.exit-key")!.TextContent).IsEqualTo("n");
		await tile.QuerySelector("button.exit-go")!.ClickAsync();
		await Assert.That(ran).IsEqualTo("goto #1210");
	}

	[Test]
	public async Task AnOccupiedDestination_ShowsACount_NeverNames()
	{
		var cut = RenderExits([Row, Ferry]);
		await Assert.That(Tile(cut, "Harbour Row").QuerySelector(".exit-pill")!.TextContent.Trim()).IsEqualTo("2 there");
		await Assert.That(Tile(cut, "Ferry Steps").QuerySelector(".exit-pill--there")).IsNull();
	}

	[Test]
	public async Task ALockedExit_IsListed_WithItsHint_AndTryingItSendsTheCommand()
	{
		string? ran = null;
		var cut = RenderExits([Customs], c => ran = c);
		var tile = Tile(cut, "Customs House");
		await Assert.That(tile.QuerySelector(".exit-pill")!.TextContent.Trim()).IsEqualTo("Locked");
		await Assert.That(tile.QuerySelector(".exit-sub")!.TextContent).IsEqualTo("Closed after dusk");
		await Assert.That(tile.ClassList).Contains("exit--locked");
		await tile.QuerySelector("button.exit-go")!.ClickAsync();
		await Assert.That(ran).IsEqualTo("goto #1211").Because("the game answers a failed lock with its @fail");
	}

	[Test]
	public async Task AClosedExit_SaysClosed()
	{
		var cut = RenderExits([Door]);
		await Assert.That(Tile(cut, "Cellar").QuerySelector(".exit-pill")!.TextContent.Trim()).IsEqualTo("Closed");
	}

	[Test]
	public async Task AnExitThatLeavesTheScene_AsksFirst_AndStayingDoesNothing()
	{
		var ran = new List<string>();
		var cut = RenderExits([Ferry], ran.Add);
		var tile = Tile(cut, "Ferry Steps");
		await Assert.That(tile.QuerySelector(".exit-pill")!.TextContent.Trim()).IsEqualTo("Leaves scene");
		await tile.QuerySelector("button.exit-go")!.ClickAsync();
		await Assert.That(ran).IsEmpty();
		await Assert.That(cut.Find("[role='alertdialog'] .exit-confirm-text").TextContent).IsEqualTo("This leaves the scene.");
		await cut.Find("button.exit-confirm-stay").ClickAsync();
		await Assert.That(cut.FindAll("[role='alertdialog']").Count).IsEqualTo(0);
		await Assert.That(ran).IsEmpty();

		await Tile(cut, "Ferry Steps").QuerySelector("button.exit-go")!.ClickAsync();
		await cut.Find("button.exit-confirm-go").ClickAsync();
		await Assert.That(ran).IsEquivalentTo(new[] { "goto #1212" });
	}

	[Test]
	public async Task APendingConfirmation_DoesNotSurviveARoomChange()
	{
		// Reviewer: the "leaves the scene" dialog outlived the move and its Go would send the old room's exit.
		var ran = new List<string>();
		var cut = RenderExits([Ferry, Row], ran.Add);
		await cut.InvokeAsync(() => cut.Instance.GoByKey("e"));
		await Assert.That(cut.FindAll("[role='alertdialog']").Count).IsEqualTo(1);
		cut.Render(p => p.Add(x => x.Exits, new[] { Customs }));
		await Assert.That(cut.FindAll("[role='alertdialog']").Count).IsEqualTo(0);
	}

	[Test]
	public async Task Escape_CancelsThePendingConfirmation()
	{
		var ran = new List<string>();
		var cut = RenderExits([Ferry], ran.Add);
		await Tile(cut, "Ferry Steps").QuerySelector("button.exit-go")!.ClickAsync();
		await cut.Find("[role='alertdialog']").KeyDownAsync("Escape");
		await Assert.That(cut.FindAll("[role='alertdialog']").Count).IsEqualTo(0);
		await Assert.That(ran).IsEmpty();
	}

	[Test]
	public async Task NoPicture_IsTheFallbackTile()
	{
		var cut = RenderExits([Out]);
		await Assert.That(Tile(cut, "Out").QuerySelector("img")).IsNull();
		await Assert.That(Tile(cut, "Out").QuerySelector(".exit-fallback")).IsNotNull();
		await Assert.That(Tile(cut, "Out").QuerySelector("kbd.exit-key")!.TextContent).IsEqualTo("out")
			.Because("the keycap is the first alias, whatever its length");
	}

	[Test]
	public async Task AVersionOneExit_WithOnlyANameAndACommand_Works()
	{
		string? ran = null;
		var v1 = new RoomExit(null, "North", "north", null, [], null, null, null, null);
		var nameless = new RoomExit(null, "", null, null, [], null, null, null, null);
		var cut = RenderExits([v1, nameless], c => ran = c);
		await Tile(cut, "North").QuerySelector("button.exit-go")!.ClickAsync();
		await Assert.That(ran).IsEqualTo("north");
		await Assert.That(Tile(cut, "Untitled").QuerySelector("button.exit-go")!.HasAttribute("disabled")).IsTrue();
	}

	[Test]
	public async Task ThePreview_HasTheDescription_TheCount_Go_AndLook()
	{
		string? ran = null;
		var cut = RenderExits([Row], c => ran = c);
		var preview = Tile(cut, "Harbour Row").QuerySelector(".exit-preview")!;
		await Assert.That(preview.QuerySelector(".exit-preview-desc")!.TextContent).IsEqualTo("A lamplit street of chandlers.");
		await Assert.That(preview.QuerySelector(".exit-preview-there")!.TextContent).IsEqualTo("2 there");
		await preview.QuerySelector("button.exit-preview-look")!.ClickAsync();
		await Assert.That(ran).IsEqualTo("look #1210");
		await Tile(cut, "Harbour Row").QuerySelector(".exit-preview button.exit-preview-go")!.ClickAsync();
		await Assert.That(ran).IsEqualTo("goto #1210");
	}

	[Test]
	public async Task AliasKeys_AreRegistered_ForSingleCharacterKeycaps_AndGo()
	{
		var ran = new List<string>();
		var cut = RenderExits([Row, Customs, Ferry, Out], ran.Add);
		var register = JSInterop.VerifyInvoke("sharpmushLayout.registerExitKeys");
		await Assert.That((string[])register.Arguments[1]!).IsEquivalentTo(new[] { "n", "w", "e" });
		await cut.InvokeAsync(() => cut.Instance.GoByKey("n"));
		await Assert.That(ran).IsEquivalentTo(new[] { "goto #1210" });
		await cut.InvokeAsync(() => cut.Instance.GoByKey("e"));
		await Assert.That(ran.Count).IsEqualTo(1).Because("the scene exit asks first, even from a key");
		await Assert.That(cut.FindAll("[role='alertdialog']").Count).IsEqualTo(1);
	}

	[Test]
	public async Task ScreenReaderMode_TurnsTheAliasKeysOff()
	{
		// WCAG 2.1.4: a letter typed to a screen reader must not walk the character out of the room.
		var cut = RenderExits([Row, Customs]);
		await Assert.That(Tile(cut, "Harbour Row").QuerySelector("[aria-keyshortcuts]")).IsNotNull();

		await cut.InvokeAsync(() => Services.GetRequiredService<ScreenReaderMode>().SetAsync(true));

		var last = JSInterop.Invocations["sharpmushLayout.registerExitKeys"][^1];
		await Assert.That((string[])last.Arguments[1]!).IsEmpty();
		await Assert.That(cut.FindAll("[aria-keyshortcuts]").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".exit-go[title]").Count).IsEqualTo(0).Because("no key to tell of");
	}

	[Test]
	public async Task Rows_AreTheMobileList_WithKeycaps()
	{
		var cut = RenderExits([Row, Customs], rows: true);
		await Assert.That(cut.FindAll(".exits--rows .exit").Count).IsEqualTo(2);
		await Assert.That(Tile(cut, "Harbour Row").QuerySelector(".exit-sub")!.TextContent).IsEqualTo("Harbour Ward");
		await Assert.That(Tile(cut, "Customs House").QuerySelector(".exit-sub")!.TextContent).IsEqualTo("Closed after dusk");
	}

	[Test]
	public async Task AnExit_NamesItsDestination_WhenItsOwnNameDoesNot()
	{
		// Grave: the room on the other side should be readable in the list, not only on hover.
		var cut = RenderExits([Out, Row, Customs]);
		await Assert.That(Tile(cut, "Out").QuerySelector(".exit-dest-name")!.TextContent).IsEqualTo("Salt Market");
		await Assert.That(Tile(cut, "Harbour Row").QuerySelector(".exit-dest")).IsNull()
			.Because("the exit's name already says where it goes");
		await Assert.That(Tile(cut, "Customs House").QuerySelector(".exit-dest")).IsNull()
			.Because("a row without a destination names none");
	}

	[Test]
	public async Task Rows_NameTheDestination_AboveTheArea()
	{
		var outward = Out with { Dest = Out.Dest! with { Area = "Harbour Ward" } };
		var cut = RenderExits([outward], rows: true);
		await Assert.That(Tile(cut, "Out").QuerySelector(".exit-dest-name")!.TextContent).IsEqualTo("Salt Market");
		await Assert.That(Tile(cut, "Out").QuerySelector(".exit-sub")!.TextContent).IsEqualTo("Harbour Ward");
	}

	[Test]
	public async Task NoExits_SaysSo()
	{
		var cut = RenderExits([]);
		await Assert.That(cut.Find(".exits-empty").TextContent).Contains("No exits");
	}
}
