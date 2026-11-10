using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

public class TerminalFrameRendererTests
{
	[Test]
	public async Task MarkupSendLinkRendersCommandForTerminalClickHandler()
	{
		var link = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown("[newbie]");
		var envelope = System.Text.Json.JsonSerializer.Serialize(new
		{
			type = "markup",
			data = System.Text.Json.Nodes.JsonNode.Parse(MarkupString.MarkupTextSerializer.Serialize(link))
		});
		var frame = TerminalFrameRenderer.Parse(envelope);

		await Assert.That(frame.Kind).IsEqualTo(TerminalFrameKind.Markup);
		await Assert.That(frame.Html).Contains("xch_cmd=\"help newbie\"");
		await Assert.That(frame.Plain).IsEqualTo("help newbie");
	}

	[Test]
	public async Task MarkupSentAsAStringStillRenders()
	{
		var envelope = System.Text.Json.JsonSerializer.Serialize(new
		{
			type = "markup",
			data = MarkupString.MarkupTextSerializer.Serialize(MString.Plain("You see a quay."))
		});
		var frame = TerminalFrameRenderer.Parse(envelope);

		await Assert.That(frame.Kind).IsEqualTo(TerminalFrameKind.Markup);
		await Assert.That(frame.Plain).IsEqualTo("You see a quay.");
	}

	[Test]
	public async Task OobEnvelopeSurfacesPackageAndData()
	{
		var frame = TerminalFrameRenderer.Parse("{\"type\":\"oob\",\"package\":\"room.contents\",\"data\":{\"who\":[\"#5\"]}}");
		await Assert.That(frame.Kind).IsEqualTo(TerminalFrameKind.Oob);
		await Assert.That(frame.Package).IsEqualTo("room.contents");
		await Assert.That(frame.DataJson).IsEqualTo("{\"who\":[\"#5\"]}");
	}

	[Test]
	public async Task LegacyJsonEnvelopeHasEmptyPackage()
	{
		var frame = TerminalFrameRenderer.Parse("{\"type\":\"json\",\"data\":{\"x\":1}}");
		await Assert.That(frame.Kind).IsEqualTo(TerminalFrameKind.Oob);
		await Assert.That(frame.Package).IsEqualTo(string.Empty);
		await Assert.That(frame.DataJson).IsEqualTo("{\"x\":1}");
	}

	private static string MarkupEnvelope(string text, bool? prompt = null, string? session = null)
	{
		var envelope = new System.Text.Json.Nodes.JsonObject
		{
			["type"] = "markup",
			["data"] = System.Text.Json.Nodes.JsonNode.Parse(MarkupString.MarkupTextSerializer.Serialize(MString.Plain(text)))
		};
		if (prompt is { } p) envelope["prompt"] = p;
		if (session is not null) envelope["session"] = session;
		return envelope.ToJsonString();
	}

	[Test]
	public async Task OrdinaryMarkupIsNoPrompt()
	{
		var frame = TerminalFrameRenderer.Parse(MarkupEnvelope("You see a quay."));

		await Assert.That(frame.Kind).IsEqualTo(TerminalFrameKind.Markup);
		await Assert.That(frame.Prompt).IsFalse();
		await Assert.That(frame.Session).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task PromptMarkupCarriesTheFlagAndItsSession()
	{
		var frame = TerminalFrameRenderer.Parse(MarkupEnvelope("Read which post?", prompt: true, session: "8f0c"));

		await Assert.That(frame.Kind).IsEqualTo(TerminalFrameKind.Markup);
		await Assert.That(frame.Prompt).IsTrue();
		await Assert.That(frame.Session).IsEqualTo("8f0c");
		await Assert.That(frame.Plain).IsEqualTo("Read which post?");
	}

	[Test]
	public async Task OneOffPromptHasNoSession()
	{
		var frame = TerminalFrameRenderer.Parse(MarkupEnvelope("Continue?", prompt: true));

		await Assert.That(frame.Prompt).IsTrue();
		await Assert.That(frame.Session).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task PromptFalseIsNoPrompt()
	{
		var frame = TerminalFrameRenderer.Parse(MarkupEnvelope("text", prompt: false, session: "8f0c"));

		await Assert.That(frame.Prompt).IsFalse();
	}

	[Test]
	[Arguments("{\"type\":\"prompt\",\"clear\":true,\"session\":\"8f0c\"}", "8f0c")]
	[Arguments("{\"type\":\"prompt\",\"clear\":true}", "")]
	public async Task PromptClearFrameNamesItsSession(string json, string session)
	{
		var frame = TerminalFrameRenderer.Parse(json);

		await Assert.That(frame.Kind).IsEqualTo(TerminalFrameKind.PromptClear);
		await Assert.That(frame.Session).IsEqualTo(session);
		await Assert.That(frame.Html).IsEqualTo(string.Empty);
	}

	[Test]
	[Arguments("{\"type\":\"prompt\"}")]
	[Arguments("{\"type\":\"prompt\",\"clear\":false}")]
	[Arguments("{\"type\":\"somethingnew\",\"clear\":true}")]
	public async Task UnknownEnvelopeIsStillPlainText(string json)
	{
		var frame = TerminalFrameRenderer.Parse(json);

		await Assert.That(frame.Kind).IsEqualTo(TerminalFrameKind.PlainText);
		await Assert.That(frame.Plain).IsEqualTo(json);
	}

	[Test]
	public async Task PlainTextKeepsItsAnsiStyling()
	{
		var lines = TerminalFrameRenderer.DecodePlainText("Welcome to \u001b[1;31mSharpMUSH\u001b[0m!\r\n");

		await Assert.That(lines.Count).IsEqualTo(1);
		await Assert.That(lines[0].Plain).IsEqualTo("Welcome to SharpMUSH!");
		await Assert.That(lines[0].Html)
			.IsEqualTo("Welcome to <span style=\"color: var(--ms-ansi-1, #aa0000)\" class=\"ms-bold\">SharpMUSH</span>!");
	}

	[Test]
	public async Task PlainTextIsHtmlEncoded()
	{
		var lines = TerminalFrameRenderer.DecodePlainText("<script>alert(1)</script> & co");

		await Assert.That(lines[0].Html).IsEqualTo("&lt;script&gt;alert(1)&lt;/script&gt; &amp; co");
	}

	/// <summary>A blank line, and a line of nothing but sequences, are left out, as the ANSI strip left them out.</summary>
	[Test]
	public async Task PlainTextIsSplitIntoLinesWithoutTheBlankOnes()
	{
		var lines = TerminalFrameRenderer.DecodePlainText("one\r\n\r\n\u001b[0m\r\ntwo");

		await Assert.That(lines.Select(l => l.Plain)).IsEquivalentTo(["one", "two"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task PlainTextStyleDoesNotCarryIntoTheNextFrame()
	{
		TerminalFrameRenderer.DecodePlainText("\u001b[31mred\n");

		await Assert.That(TerminalFrameRenderer.DecodePlainText("plain\n")[0].Html).IsEqualTo("plain");
	}

	[Test]
	public async Task CarriageReturnOverwritesTheLine()
	{
		var lines = TerminalFrameRenderer.DecodePlainText("Loading 10%\rLoading 100%\r\n");

		await Assert.That(lines[0].Plain).IsEqualTo("Loading 100%");
	}

	[Test]
	public async Task TitleSequenceIsReadOutOfTheText()
	{
		var lines = TerminalFrameRenderer.DecodePlainText("\u001b]2;The Hall\u0007Hello\n");

		await Assert.That(lines[0].Plain).IsEqualTo("Hello");
	}

	[Test]
	public async Task LineDrawingCharactersAreDrawn()
	{
		var lines = TerminalFrameRenderer.DecodePlainText("\u001b(0lqqk\u001b(B box");

		await Assert.That(lines[0].Plain).IsEqualTo("┌──┐ box");
	}

	[Test]
	public async Task HyperlinkIsClickable()
	{
		var lines = TerminalFrameRenderer.DecodePlainText("see \u001b]8;;https://example.com/x\u001b\\here\u001b]8;;\u001b\\ ok");

		await Assert.That(lines[0].Plain).IsEqualTo("see here ok");
		await Assert.That(lines[0].Html)
			.IsEqualTo("see <a href=\"https://example.com/x\" target=\"_blank\" rel=\"noopener noreferrer\">here</a> ok");
	}

	[Test]
	public async Task ScriptHyperlinkIsOnlyText()
	{
		var lines = TerminalFrameRenderer.DecodePlainText("bad \u001b]8;;javascript:alert(1)\u001b\\x\u001b]8;;\u001b\\");

		await Assert.That(lines[0].Html).IsEqualTo("bad x");
	}

	[Test]
	public async Task CommandLinkRendersForTerminalClickHandler()
	{
		var lines = TerminalFrameRenderer.DecodePlainText("\u001b]68;1;SEND;look\u0007\u001b[4mlook\u001b[24m");

		await Assert.That(lines[0].Html).Contains("xch_cmd=\"look\"");
	}
}
