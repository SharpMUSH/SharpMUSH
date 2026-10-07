using Bunit;
using MarkupString;
using MarkupString.Ansi;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Play;

namespace SharpMUSH.Tests.BUnit.Components.Play;

/// <summary>
/// README §5.8 / §4.11 composer: Say · OOC · Emit · Command chips, the field and Send. The types map to
/// say, the game's ooc command, @emit and the raw command; composed text is sent as decompose() writes it, so
/// the parser hands back exactly what was shown, and the softcode switch sends what was typed as softcode. The draft and mode are kept per character, and Command walks the command history.
/// </summary>
public class PlayComposerTests : BunitContext
{
	private readonly List<string> _sent = [];

	public PlayComposerTests()
	{
		Services.AddLocalization();
		Services.AddMudServices();
		Services.AddSingleton<SharpMUSH.Client.Services.CommandHistory>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private IRenderedComponent<PlayComposer> RenderComposer(bool connected = true, string? draftKey = null) =>
		Render<PlayComposer>(p => p.Add(x => x.Connected, connected).Add(x => x.OnSend, c => _sent.Add(c))
			.Add(x => x.DraftKey, draftKey));

	private static Task Type(IRenderedComponent<PlayComposer> cut, string text) => cut.Find("textarea").InputAsync(text);

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
		await cut.FindAll("[role=radio]")[2].ClickAsync();
		await Assert.That(cut.Find("textarea").GetAttribute("placeholder")).IsEqualTo("Write what happens…");
		await Type(cut, "Ilsa leans back; waits.\n  Then speaks.");
		await cut.Find("button.composer-send").ClickAsync();
		await Assert.That(_sent).IsEquivalentTo(new[] { @"@emit Ilsa leans back\; waits.%r %bThen speaks." });
		await Assert.That(cut.Find("textarea").GetAttribute("value") ?? string.Empty).IsEmpty();
	}

	[Test]
	public async Task SayAndOoc_UseTheirCommands()
	{
		var cut = RenderComposer();
		await Type(cut, "hello");
		await cut.Find("button.composer-send").ClickAsync();
		await cut.FindAll("[role=radio]")[1].ClickAsync();
		await Type(cut, "brb, making tea");
		await cut.Find("button.composer-send").ClickAsync();
		await Assert.That(_sent).IsEquivalentTo(new[] { "say hello", @"ooc brb\, making tea" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task TheDraftAndMode_AreKeptForTheCharacter_AndReadBack()
	{
		var cut = RenderComposer(draftKey: "play.draft.Ilsa");
		await cut.FindAll("[role=radio]")[2].ClickAsync();
		await Type(cut, "half a pose");
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
	public async Task SwitchingCharacter_SwapsInThatCharactersDraft()
	{
		JSInterop.Setup<string?>("localStorage.getItem", "play.draft.Tomas").SetResult("""{"Type":"ooc","Text":"Tomas's note"}""");
		var cut = RenderComposer(draftKey: "play.draft.Ilsa");
		await Type(cut, "Ilsa's half pose");
		cut.Render(p => p.Add(x => x.DraftKey, "play.draft.Tomas"));
		cut.WaitForAssertion(() =>
		{
			if (cut.Find("textarea").GetAttribute("value") != "Tomas's note") throw new InvalidOperationException("Tomas's draft not shown yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll("[role=radio]")[1].GetAttribute("aria-checked")).IsEqualTo("true");
		await Assert.That(JSInterop.Invocations.Where(i => i.Identifier == "localStorage.setItem")
				.Any(i => (string?)i.Arguments[0] == "play.draft.Tomas" && ((string?)i.Arguments[1] ?? "").Contains("Ilsa")))
			.IsFalse().Because("Ilsa's words are never kept as Tomas's");
		await Assert.That(JSInterop.Invocations.Any(i => i.Identifier == "localStorage.removeItem" && (string?)i.Arguments[0] == "play.draft.Tomas"))
			.IsFalse().Because("emptying the field for the switch must not delete the draft about to be read");

		cut.Render(p => p.Add(x => x.DraftKey, "play.draft.Carol"));
		cut.WaitForAssertion(() =>
		{
			if (cut.Find("textarea").GetAttribute("value") is { Length: > 0 }) throw new InvalidOperationException("field not cleared yet");
		}, TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task TheExpandButton_GrowsTheField_AndBack()
	{
		var cut = RenderComposer();
		var expand = cut.Find("button.composer-expand");
		await Assert.That(expand.GetAttribute("aria-label")).IsEqualTo("Expand the input");
		await expand.ClickAsync();
		await Assert.That(cut.Find(".composer").ClassList).Contains("composer--expanded");
		await Assert.That(cut.Find("button.composer-expand").GetAttribute("aria-pressed")).IsEqualTo("true");
		await cut.Find("button.composer-expand").ClickAsync();
		await Assert.That(cut.Find(".composer").ClassList).DoesNotContain("composer--expanded");
	}

	private const string Lorem =
		"Lorem ipsum dolor sit amet, consectetur adipiscing elit.\n\n"
		+ "Sed do eiusmod tempor incididunt ut labore.\n\n"
		+ "Ut enim ad minim veniam, quis nostrud exercitation.";

	[Test]
	public async Task LineBreaks_TravelAsR_ByDefault_SoParagraphsArriveWhole()
	{
		var cut = RenderComposer();
		await Assert.That(cut.Find("button.composer-breaks").GetAttribute("aria-pressed")).IsEqualTo("true");
		await Assert.That(cut.Find("button.composer-breaks").GetAttribute("aria-label")).IsEqualTo("Line breaks as %r");
		await Type(cut, Lorem);
		await cut.Find("button.composer-send").ClickAsync();
		await Assert.That(_sent).IsEquivalentTo(new[]
		{
			@"say Lorem ipsum dolor sit amet\, consectetur adipiscing elit.%r%r"
			+ "Sed do eiusmod tempor incididunt ut labore.%r%r"
			+ @"Ut enim ad minim veniam\, quis nostrud exercitation."
		});
	}

	[Test]
	public async Task ACommand_KeepsItsLineBreaks_AsR()
	{
		var cut = RenderComposer();
		await cut.FindAll("[role=radio]")[3].ClickAsync();
		await Type(cut, "@desc me=First line.\r\nSecond line.");
		await cut.Find("button.composer-send").ClickAsync();
		await Assert.That(_sent).IsEquivalentTo(new[] { "@desc me=First line.%rSecond line." });
	}

	[Test]
	public async Task WithTheToggleOff_EachLine_IsSentOnItsOwn_AndTheChoiceIsKept()
	{
		var cut = RenderComposer();
		await cut.Find("button.composer-breaks").ClickAsync();
		await Assert.That(cut.Find("button.composer-breaks").GetAttribute("aria-pressed")).IsEqualTo("false");
		var kept = JSInterop.Invocations.Last(i => i.Identifier == "localStorage.setItem").Arguments;
		await Assert.That((string?)kept[0]).IsEqualTo("play.composer.breaks");
		await Assert.That((string?)kept[1]).IsEqualTo("off");
		await Type(cut, Lorem);
		await cut.Find("button.composer-send").ClickAsync();
		await Assert.That(_sent).IsEquivalentTo(new[]
		{
			@"say Lorem ipsum dolor sit amet\, consectetur adipiscing elit.",
			"say Sed do eiusmod tempor incididunt ut labore.",
			@"say Ut enim ad minim veniam\, quis nostrud exercitation.",
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);

		JSInterop.Setup<string?>("localStorage.getItem", "play.composer.breaks").SetResult("off");
		var again = RenderComposer();
		again.WaitForAssertion(() =>
		{
			if (again.Find("button.composer-breaks").GetAttribute("aria-pressed") != "false") throw new InvalidOperationException("choice not read back yet");
		}, TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task InCommand_UpAndDown_WalkTheHistory()
	{
		var cut = RenderComposer();
		await cut.FindAll("[role=radio]")[3].ClickAsync();
		foreach (var command in new[] { "look", "+who" })
		{
			await Type(cut, command);
			await cut.Find("button.composer-send").ClickAsync();
		}
		await cut.Find("textarea").KeyDownAsync("ArrowUp");
		await Assert.That(cut.Find("textarea").GetAttribute("value")).IsEqualTo("+who");
		await cut.Find("textarea").KeyDownAsync("ArrowUp");
		await Assert.That(cut.Find("textarea").GetAttribute("value")).IsEqualTo("look");
		await cut.Find("textarea").KeyDownAsync("ArrowDown");
		await Assert.That(cut.Find("textarea").GetAttribute("value")).IsEqualTo("+who");
	}

	[Test]
	public async Task ACommand_IsSentAsTyped()
	{
		var cut = RenderComposer();
		await cut.FindAll("[role=radio]")[3].ClickAsync();
		await Type(cut, "  +who  ");
		await cut.Find("button.composer-send").ClickAsync();
		await Assert.That(_sent).IsEquivalentTo(new[] { "+who" });
	}

	[Test]
	public async Task Enter_Sends_ThroughTheListenerTheComposerRegisters()
	{
		// Enter sends and Shift+Enter is a new line (plan Task 6). The key is decided in layout.js, so the
		// browser's newline is prevented only for the Enter that sends.
		var cut = RenderComposer();
		JSInterop.VerifyInvoke("sharpmushLayout.composerEnter");
		await Type(cut, "hello");
		await cut.InvokeAsync(() => cut.Instance.SendFromEnter());
		await Assert.That(_sent).IsEquivalentTo(new[] { "say hello" });
		await Assert.That(cut.Find("textarea").GetAttribute("placeholder")).IsEqualTo("Say something…");
	}

	[Test]
	public async Task CtrlEnter_StillSends()
	{
		var cut = RenderComposer();
		await Type(cut, "hello");
		await cut.Find("textarea").KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true });
		await Assert.That(_sent).IsEquivalentTo(new[] { "say hello" });
	}

	[Test]
	public async Task NothingToSend_OrNotConnected_DisablesSend()
	{
		var empty = RenderComposer();
		await Assert.That(empty.Find("button.composer-send").HasAttribute("disabled")).IsTrue();
		await Type(empty, "   ");
		await Assert.That(empty.Find("button.composer-send").HasAttribute("disabled")).IsTrue();

		var offline = RenderComposer(connected: false);
		await Type(offline, "a pose");
		await Assert.That(offline.Find("button.composer-send").HasAttribute("disabled")).IsTrue();
		await offline.Find("textarea").KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true });
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

	[Test]
	public async Task TypedText_IsSentAsShown_EvenWhenItLooksLikeSoftcode()
	{
		var cut = RenderComposer();
		await Type(cut, "100% [OOC] sure");
		await Assert.That(cut.FindAll(".fi-notice").Count).IsEqualTo(0).Because("brackets and a percent sign are prose");
		await cut.Find("button.composer-send").ClickAsync();
		await Assert.That(_sent).IsEquivalentTo(new[] { @"say 100\% \[OOC\] sure" });
	}

	[Test]
	public async Task Softcode_IsNoticed_AndTheSwitchSendsItAsTyped_AndIsKept()
	{
		var cut = RenderComposer();
		await Type(cut, "[ansi(hr,Hello)]%rthere");
		await Assert.That(cut.Find(".fi-notice").TextContent).Contains("[ansi(");
		await cut.Find(".fi-notice-action").ClickAsync();
		var kept = JSInterop.Invocations.Last(i => i.Identifier == "localStorage.setItem" && (string?)i.Arguments[0] == "play.composer.raw").Arguments;
		await Assert.That((string?)kept[1]).IsEqualTo("on");
		await Assert.That(cut.FindAll(".fi-overlay").Count).IsEqualTo(0);
		await cut.Find("button.composer-send").ClickAsync();
		await Assert.That(_sent).IsEquivalentTo(new[] { "say [ansi(hr,Hello)]%rthere" });
	}

	[Test]
	public async Task AStyledDraft_IsKeptStyled_AndReadBack()
	{
		var styled = MarkupText.Concat(MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Standard(2, false)), "green"), MarkupText.Plain(" words"));
		var json = System.Text.Json.JsonSerializer.Serialize(new { Type = "say", Text = "green words", Markup = MarkupTextSerializer.Serialize(styled) });
		JSInterop.Setup<string?>("localStorage.getItem", "play.draft.Ilsa").SetResult(json);
		var cut = RenderComposer(draftKey: "play.draft.Ilsa");
		cut.WaitForAssertion(() =>
		{
			if (cut.Find("textarea").GetAttribute("value") != "green words") throw new InvalidOperationException("draft not read back yet");
		}, TimeSpan.FromSeconds(5));
		await cut.Find("button.composer-send").ClickAsync();
		await Assert.That(_sent).IsEquivalentTo(new[] { "say [ansi(g,green)]%bwords" });
	}
}
