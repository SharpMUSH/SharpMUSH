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
		await Assert.That(frame.Plain).IsEqualTo("newbie");
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
}
