using SharpMUSH.ConnectionServer.ProtocolHandlers;
using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;
using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Covers the wire-format rendering that moved out of NotifyService: serialized markup is rendered
/// to ANSI/Pueblo/MXP for terminal connections (per <see cref="ProtocolCapabilities.Format"/>) and
/// forwarded as a markup envelope for WebSocket (portal) connections.
/// </summary>
public partial class MarkupOutputRendererTests
{
	private const string Raw = "<send href=\"look\">Tom & \"Sue\"</send>";

	private static string StripAnsi(string text) => AnsiEscape().Replace(text, string.Empty);

	[GeneratedRegex("\u001b\\[[0-9;]*m")]
	private static partial Regex AnsiEscape();

	private static ConnectionServerService.ConnectionData Connection(
		OutputFormat format = OutputFormat.Ansi,
		string connectionType = "telnet") =>
		new(
			Handle: 1,
			PlayerDbRef: null,
			State: ConnectionServerService.ConnectionState.Connected,
			OutputFunction: _ => ValueTask.CompletedTask,
			PromptOutputFunction: _ => ValueTask.CompletedTask,
			EncodingFunction: () => Encoding.UTF8,
			DisconnectFunction: () => { },
			GMCPFunction: null,
			Capabilities: new ProtocolCapabilities(Format: format),
			Preferences: null,
			ConnectionType: connectionType);

	[Test]
	public async Task Pueblo_HtmlEncodesPlainText()
	{
		var markup = MarkupTextSerializer.Serialize(MarkupText.Plain(Raw));
		var result = new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Pueblo));
		var text = Encoding.UTF8.GetString(result.Data);

		await Assert.That(result.ApplyOutputTransform).IsTrue();
		// Only < > & are entities: a quote is markup inside an attribute value, and nothing puts plain
		// text there. MarkupString 2.0.0 stopped encoding " and ' for that reason.
		await Assert.That(text).Contains("&lt;send href=\"look\"&gt;Tom &amp; \"Sue\"&lt;/send&gt;");
	}

	[Test]
	public async Task Mxp_PrefixesLinesAndHtmlEncodes()
	{
		var markup = MarkupTextSerializer.Serialize(MarkupText.Plain(Raw));
		var result = new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Mxp));
		var text = Encoding.UTF8.GetString(result.Data);

		await Assert.That(result.ApplyOutputTransform).IsTrue();
		await Assert.That(text).Contains(
			$"{ProtocolConstants.MxpLineSecure}&lt;send href=\"look\"&gt;Tom &amp; \"Sue\"&lt;/send&gt;");
	}

	[Test]
	public async Task Mxp_HelpLinksUseSecureModeOnEveryLine()
	{
		var link = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown("[newbie]");
		var markup = MarkupTextSerializer.Serialize(MarkupText.Concat(MarkupText.Concat(link, MarkupText.Plain("\n")), link));
		var result = new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Mxp));
		var lines = Encoding.UTF8.GetString(result.Data).Split("\r\n");

		await Assert.That(lines.Length).IsEqualTo(2);
		foreach (var line in lines)
		{
			await Assert.That(line.StartsWith(ProtocolConstants.MxpLineSecure)).IsTrue();
			await Assert.That(line).Contains("<SEND HREF=\"help newbie\">newbie</SEND>");
		}
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task Mxp_ModeSurvivesDisablingAnsi(bool supportsAnsi)
	{
		var capabilities = new ProtocolCapabilities(SupportsAnsi: supportsAnsi, Format: OutputFormat.Mxp);
		var preferences = supportsAnsi ? new PlayerOutputPreferences(AnsiEnabled: false) : null;
		var transform = new OutputTransformService(
			Microsoft.Extensions.Logging.Abstractions.NullLogger<OutputTransformService>.Instance);
		var text = Encoding.UTF8.GetString(transform.Transform(
			Encoding.UTF8.GetBytes("\x1b[1z<SEND HREF=\"help newbie\">\x1b[31mnewbie\x1b[0m</SEND>"),
			capabilities, preferences));

		await Assert.That(text).IsEqualTo("\x1b[1z<SEND HREF=\"help newbie\">newbie</SEND>");
	}

	[Test]
	public async Task Ansi_KeepsRawText()
	{
		var markup = MarkupTextSerializer.Serialize(MarkupText.Plain(Raw));
		var result = new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Ansi));
		var text = Encoding.UTF8.GetString(result.Data);

		await Assert.That(result.ApplyOutputTransform).IsTrue();
		await Assert.That(text).Contains(Raw);
	}

	/// <summary>
	/// An HtmlMarkup span (what `look` wraps every exit name in) must not reach a client that
	/// negotiated no Pueblo/MXP: a plain telnet client has no idea what &lt;send&gt; is and prints
	/// the tag literally. Rendering an ANSI connection natively rather than as ANSI leaked the tags.
	/// </summary>
	[Test]
	public async Task Ansi_StripsHtmlMarkupTags()
	{
		var send = HtmlMarkup.Create("send", "href=\"north\" hint=\"Go north\"");
		var markup = MarkupTextSerializer.Serialize(MarkupText.Wrap(send, MarkupText.Plain("north")));

		var result = new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Ansi));
		var text = Encoding.UTF8.GetString(result.Data);

		await Assert.That(text).DoesNotContain("<send");
		await Assert.That(text).DoesNotContain("</send>");
		// The trailing ANSI reset the ANSI strategy appends to any markup-bearing string is expected.
		await Assert.That(StripAnsi(text)).IsEqualTo("north");
	}

	/// <summary>
	/// The exits line as `look` actually builds it: an unmarked label followed by send-wrapped exit
	/// names. The leading plain run means the first markup in the string is the HtmlMarkup, which is
	/// what selected the native (tag-emitting) render strategy.
	/// </summary>
	[Test]
	public async Task Ansi_StripsHtmlMarkupTagsInAMixedLine()
	{
		var send = HtmlMarkup.Create("send", "href=\"north\" hint=\"Go north\"");
		var line = MarkupText.Concat(
			MarkupText.Plain("Obvious exits:\n"),
			MarkupText.Wrap(send, MarkupText.Plain("north")));

		var result = new MarkupOutputRenderer().Render(MarkupTextSerializer.Serialize(line), Connection(OutputFormat.Ansi));
		var text = Encoding.UTF8.GetString(result.Data);

		await Assert.That(text).DoesNotContain("<send");
		await Assert.That(StripAnsi(text)).IsEqualTo("Obvious exits:\r\nnorth");
	}

	/// <summary>
	/// Colour must survive the ANSI render — the fix must not turn styled output into plain text.
	/// </summary>
	[Test]
	public async Task Ansi_KeepsAnsiMarkupAlongsideStrippedHtml()
	{
		var send = HtmlMarkup.Create("send", "href=\"north\"");
		var red = AnsiMarkup.Create(foreground: Color.Red.ToAnsiColor());
		var line = MarkupText.Wrap(MarkupSet.Of([red, send]), "north");

		var result = new MarkupOutputRenderer().Render(MarkupTextSerializer.Serialize(line), Connection(OutputFormat.Ansi));
		var text = Encoding.UTF8.GetString(result.Data);

		await Assert.That(text).DoesNotContain("<send");
		await Assert.That(text).Contains("\x1b[");
		await Assert.That(text).Contains("north");
	}

	[Test]
	public async Task WebSocket_WrapsMarkupEnvelopeWithoutTransform()
	{
		var markup = MarkupTextSerializer.Serialize(MarkupText.Plain(Raw));
		var result = new MarkupOutputRenderer().Render(markup, Connection(connectionType: "websocket"));
		var text = Encoding.UTF8.GetString(result.Data);

		// The envelope is JSON the browser renders itself, so the ANSI/charset transform must be skipped.
		await Assert.That(result.ApplyOutputTransform).IsFalse();

		using var doc = JsonDocument.Parse(text);
		var root = doc.RootElement;
		await Assert.That(root.GetProperty("type").GetString()).IsEqualTo("markup");

		var data = root.GetProperty("data").GetString()!;
		await Assert.That(MarkupTextSerializer.Deserialize(data).ToPlainText()).IsEqualTo(Raw);
	}
}
