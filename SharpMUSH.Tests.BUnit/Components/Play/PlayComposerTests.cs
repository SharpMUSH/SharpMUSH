using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Play;

namespace SharpMUSH.Tests.BUnit.Components.Play;

/// <summary>
/// README §5.8 / §4.11 composer: Say · OOC · Emit · Command chips, the field and Send. The types map to
/// say, the game's ooc command, @emit and the raw command; composed text is encoded so the parser hands
/// it back unchanged. The draft and mode are kept per character, and Command walks the command history.
/// </summary>
public class PlayComposerTests : BunitContext
{
	private readonly List<string> _sent = [];

	public PlayComposerTests()
	{
		Services.AddLocalization();
		Services.AddSingleton<SharpMUSH.Client.Services.CommandHistory>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private IRenderedComponent<PlayComposer> RenderComposer(bool connected = true, string? draftKey = null) =>
		Render<PlayComposer>(p => p.Add(x => x.Connected, connected).Add(x => x.OnSend, c => _sent.Add(c))
			.Add(x => x.DraftKey, draftKey));

	private static void Type(IRenderedComponent<PlayComposer> cut, string text) => cut.Find("textarea").Input(text);

	[Test]
	public async Task TheChips_AreTheFourTypes_SayFirst()
	{
		var cut = RenderComposer();
		await Assert.That(cut.Find("[role=radiogroup]").GetAttribute("aria-label")).IsEqualTo("Message type");
		await Assert.That(cut.FindAll("[role=radio]").Select(r => r.TextContent).ToList())
			.IsEquivalentTo(new[] { "Say", "OOC", "Emit", "Command" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(cut.Find("textarea").GetAttribute("placeholder")).IsEqualTo("Say something…");
	}

	[Test]
	public async Task AnEmit_IsSentEncoded_AndTheFieldEmpties()
	{
		var cut = RenderComposer();
		cut.FindAll("[role=radio]")[2].Click();
		await Assert.That(cut.Find("textarea").GetAttribute("placeholder")).IsEqualTo("Write what happens…");
		Type(cut, "Ilsa leans back; waits.\n  Then speaks.");
		cut.Find("button.composer-send").Click();
		await Assert.That(_sent).IsEquivalentTo(new[] { "@emit Ilsa leans back%; waits.%r%b%bThen speaks." });
		await Assert.That(cut.Find("textarea").GetAttribute("value") ?? string.Empty).IsEmpty();
	}

	[Test]
	public async Task SayAndOoc_UseTheirCommands()
	{
		var cut = RenderComposer();
		Type(cut, "hello");
		cut.Find("button.composer-send").Click();
		cut.FindAll("[role=radio]")[1].Click();
		Type(cut, "brb, making tea");
		cut.Find("button.composer-send").Click();
		await Assert.That(_sent).IsEquivalentTo(new[] { "say hello", "ooc brb, making tea" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task TheDraftAndMode_AreKeptForTheCharacter_AndReadBack()
	{
		var cut = RenderComposer(draftKey: "play.draft.Ilsa");
		cut.FindAll("[role=radio]")[2].Click();
		Type(cut, "half a pose");
		var kept = JSInterop.Invocations.Last(i => i.Identifier == "localStorage.setItem").Arguments;
		await Assert.That((string?)kept[0]).IsEqualTo("play.draft.Ilsa");
		await Assert.That((string?)kept[1]).Contains("half a pose");

		JSInterop.Setup<string?>("localStorage.getItem", "play.draft.Ilsa").SetResult("""{"Type":"emit","Text":"half a pose"}""");
		var again = RenderComposer(draftKey: "play.draft.Ilsa");
		again.WaitForAssertion(() =>
		{
			if (again.Find("textarea").GetAttribute("value") != "half a pose") throw new InvalidOperationException("draft not read back yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(again.FindAll("[role=radio]")[2].GetAttribute("aria-checked")).IsEqualTo("true");
	}

	[Test]
	public async Task TheExpandButton_GrowsTheField_AndBack()
	{
		var cut = RenderComposer();
		var expand = cut.Find("button.composer-expand");
		await Assert.That(expand.GetAttribute("aria-label")).IsEqualTo("Expand the input");
		expand.Click();
		await Assert.That(cut.Find(".composer").ClassList).Contains("composer--expanded");
		await Assert.That(cut.Find("button.composer-expand").GetAttribute("aria-pressed")).IsEqualTo("true");
		cut.Find("button.composer-expand").Click();
		await Assert.That(cut.Find(".composer").ClassList).DoesNotContain("composer--expanded");
	}

	[Test]
	public async Task InCommand_UpAndDown_WalkTheHistory()
	{
		var cut = RenderComposer();
		cut.FindAll("[role=radio]")[3].Click();
		foreach (var command in new[] { "look", "+who" })
		{
			Type(cut, command);
			cut.Find("button.composer-send").Click();
		}
		cut.Find("textarea").KeyDown("ArrowUp");
		await Assert.That(cut.Find("textarea").GetAttribute("value")).IsEqualTo("+who");
		cut.Find("textarea").KeyDown("ArrowUp");
		await Assert.That(cut.Find("textarea").GetAttribute("value")).IsEqualTo("look");
		cut.Find("textarea").KeyDown("ArrowDown");
		await Assert.That(cut.Find("textarea").GetAttribute("value")).IsEqualTo("+who");
	}

	[Test]
	public async Task ACommand_IsSentAsTyped()
	{
		var cut = RenderComposer();
		cut.FindAll("[role=radio]")[3].Click();
		Type(cut, "  +who  ");
		cut.Find("button.composer-send").Click();
		await Assert.That(_sent).IsEquivalentTo(new[] { "+who" });
	}

	[Test]
	public async Task Enter_Sends_ThroughTheListenerTheComposerRegisters()
	{
		// Enter sends and Shift+Enter is a new line (plan Task 6). The key is decided in layout.js, so the
		// browser's newline is prevented only for the Enter that sends.
		var cut = RenderComposer();
		JSInterop.VerifyInvoke("sharpmushLayout.composerEnter");
		Type(cut, "hello");
		await cut.InvokeAsync(() => cut.Instance.SendFromEnter());
		await Assert.That(_sent).IsEquivalentTo(new[] { "say hello" });
		await Assert.That(cut.Find("textarea").GetAttribute("placeholder")).IsEqualTo("Say something…");
	}

	[Test]
	public async Task CtrlEnter_StillSends()
	{
		var cut = RenderComposer();
		Type(cut, "hello");
		cut.Find("textarea").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true });
		await Assert.That(_sent).IsEquivalentTo(new[] { "say hello" });
	}

	[Test]
	public async Task NothingToSend_OrNotConnected_DisablesSend()
	{
		var empty = RenderComposer();
		await Assert.That(empty.Find("button.composer-send").HasAttribute("disabled")).IsTrue();
		Type(empty, "   ");
		await Assert.That(empty.Find("button.composer-send").HasAttribute("disabled")).IsTrue();

		var offline = RenderComposer(connected: false);
		Type(offline, "a pose");
		await Assert.That(offline.Find("button.composer-send").HasAttribute("disabled")).IsTrue();
		offline.Find("textarea").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true });
		await Assert.That(_sent).IsEmpty();
	}

	[Test]
	public async Task StartingAPage_PicksCommand_AndPrefillsIt()
	{
		var cut = RenderComposer();
		await cut.InvokeAsync(() => cut.Instance.StartPageAsync("Tomas Reyes"));
		await Assert.That(cut.FindAll("[role=radio]")[3].GetAttribute("aria-checked")).IsEqualTo("true");
		await Assert.That(cut.Find("textarea").GetAttribute("value")).IsEqualTo("page Tomas Reyes=");
	}
}
