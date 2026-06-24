using System.Text;
using System.Text.Json;
using MarkupString;
using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Covers the wire-format rendering that moved out of NotifyService: serialized markup is rendered
/// to ANSI/Pueblo/MXP for terminal connections (per <see cref="ProtocolCapabilities.Format"/>) and
/// forwarded as a markup envelope for WebSocket (portal) connections.
/// </summary>
public class MarkupOutputRendererTests
{
	private const string Raw = "<send href=\"look\">Tom & \"Sue\"</send>";

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
		var markup = MModule.serialize(MModule.single(Raw));
		var result = new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Pueblo));
		var text = Encoding.UTF8.GetString(result.Data);

		await Assert.That(result.ApplyOutputTransform).IsTrue();
		await Assert.That(text).Contains("&lt;send href=&quot;look&quot;&gt;Tom &amp; &quot;Sue&quot;&lt;/send&gt;");
	}

	[Test]
	public async Task Mxp_PrefixesLinesAndHtmlEncodes()
	{
		var markup = MModule.serialize(MModule.single(Raw));
		var result = new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Mxp));
		var text = Encoding.UTF8.GetString(result.Data);

		await Assert.That(result.ApplyOutputTransform).IsTrue();
		await Assert.That(text).Contains(
			$"{ErrorMessages.Notifications.MxpLineOpen}&lt;send href=&quot;look&quot;&gt;Tom &amp; &quot;Sue&quot;&lt;/send&gt;");
	}

	[Test]
	public async Task Ansi_KeepsRawText()
	{
		var markup = MModule.serialize(MModule.single(Raw));
		var result = new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Ansi));
		var text = Encoding.UTF8.GetString(result.Data);

		await Assert.That(result.ApplyOutputTransform).IsTrue();
		await Assert.That(text).Contains(Raw);
	}

	[Test]
	public async Task WebSocket_WrapsMarkupEnvelopeWithoutTransform()
	{
		var markup = MModule.serialize(MModule.single(Raw));
		var result = new MarkupOutputRenderer().Render(markup, Connection(connectionType: "websocket"));
		var text = Encoding.UTF8.GetString(result.Data);

		// The envelope is JSON the browser renders itself, so the ANSI/charset transform must be skipped.
		await Assert.That(result.ApplyOutputTransform).IsFalse();

		using var doc = JsonDocument.Parse(text);
		var root = doc.RootElement;
		await Assert.That(root.GetProperty("type").GetString()).IsEqualTo("markup");

		var data = root.GetProperty("data").GetString()!;
		await Assert.That(MModule.deserialize(data).ToPlainText()).IsEqualTo(Raw);
	}
}
