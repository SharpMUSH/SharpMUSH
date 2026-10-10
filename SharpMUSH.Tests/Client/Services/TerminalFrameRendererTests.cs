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
			data = MarkupString.MarkupTextSerializer.Serialize(link)
		});
		var frame = TerminalFrameRenderer.Parse(envelope);

		await Assert.That(frame.Kind).IsEqualTo(TerminalFrameKind.Markup);
		await Assert.That(frame.Html).Contains("xch_cmd=\"help newbie\"");
		await Assert.That(frame.Plain).IsEqualTo("help newbie");
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
			["data"] = MarkupString.MarkupTextSerializer.Serialize(MString.Plain(text))
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
}
