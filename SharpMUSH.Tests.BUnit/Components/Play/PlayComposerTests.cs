using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Play;

namespace SharpMUSH.Tests.BUnit.Components.Play;

/// <summary>
/// README §5.8 / §4.11 composer: Pose · Say · OOC · Command chips, the field and Send. The types map to
/// pose, say, the game's ooc command and the raw command; composed text is encoded so the parser hands
/// it back unchanged.
/// </summary>
public class PlayComposerTests : BunitContext
{
	private readonly List<string> _sent = [];

	public PlayComposerTests()
	{
		Services.AddLocalization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private IRenderedComponent<PlayComposer> RenderComposer(bool connected = true) =>
		Render<PlayComposer>(p => p.Add(x => x.Connected, connected).Add(x => x.OnSend, c => _sent.Add(c)));

	private static void Type(IRenderedComponent<PlayComposer> cut, string text) => cut.Find("textarea").Input(text);

	[Test]
	public async Task TheChips_AreTheFourTypes_PoseFirst()
	{
		var cut = RenderComposer();
		await Assert.That(cut.Find("[role=radiogroup]").GetAttribute("aria-label")).IsEqualTo("Message type");
		await Assert.That(cut.FindAll("[role=radio]").Select(r => r.TextContent).ToList())
			.IsEquivalentTo(new[] { "Pose", "Say", "OOC", "Command" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(cut.Find("textarea").GetAttribute("placeholder")).IsEqualTo("Write your pose…");
	}

	[Test]
	public async Task APose_IsSentEncoded_AndTheFieldEmpties()
	{
		var cut = RenderComposer();
		Type(cut, "leans back; waits.\n  Then speaks.");
		cut.Find("button.composer-send").Click();
		await Assert.That(_sent).IsEquivalentTo(new[] { "pose leans back%; waits.%r%b%bThen speaks." });
		await Assert.That(cut.Find("textarea").GetAttribute("value") ?? string.Empty).IsEmpty();
	}

	[Test]
	public async Task SayAndOoc_UseTheirCommands()
	{
		var cut = RenderComposer();
		cut.FindAll("[role=radio]")[1].Click();
		Type(cut, "hello");
		cut.Find("button.composer-send").Click();
		cut.FindAll("[role=radio]")[2].Click();
		Type(cut, "brb, making tea");
		cut.Find("button.composer-send").Click();
		await Assert.That(_sent).IsEquivalentTo(new[] { "say hello", "ooc brb, making tea" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
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
	public async Task CtrlEnter_Sends_ButEnterIsANewLine()
	{
		var cut = RenderComposer();
		Type(cut, "a pose");
		cut.Find("textarea").KeyDown("Enter");
		await Assert.That(_sent).IsEmpty().Because("a pose is prose, written over several lines");
		cut.Find("textarea").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true });
		await Assert.That(_sent).IsEquivalentTo(new[] { "pose a pose" });
		Type(cut, "another");
		cut.Find("textarea").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", MetaKey = true });
		await Assert.That(_sent.Count).IsEqualTo(2);
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
