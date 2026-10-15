using System.Text;
using System.Text.Json;
using MarkupString;
using MarkupString.Ansi;
using SharpMUSH.Library.Markup;
using SharpMUSH.RenderingWorker.Services;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A list separator is data and is sent as its text, unless the reader set <c>SHOW_SEPARATORS</c>: then each one
/// is drawn in reverse video, on a terminal and in the portal.
/// </summary>
public class ListSeparatorRenderingTests
{
	private static readonly string Markup = MarkupTextSerializer.Serialize(
		MarkupText.Concat([MarkupText.Plain("a|b"), ListSeparators.Mark(MarkupText.Plain("|")), MarkupText.Plain("c")]));

	private static ConnectionServerService.ConnectionData Connection(bool showSeparators, string connectionType = "telnet") =>
		new(
			Handle: 1,
			PlayerDbRef: null,
			State: ConnectionServerService.ConnectionState.Connected,
			OutputFunction: _ => ValueTask.CompletedTask,
			PromptOutputFunction: _ => ValueTask.CompletedTask,
			EncodingFunction: () => Encoding.UTF8,
			DisconnectFunction: () => { },
			GMCPFunction: null,
			Capabilities: new ProtocolCapabilities(),
			Preferences: new PlayerOutputPreferences(ShowSeparators: showSeparators),
			ConnectionType: connectionType);

	private static string Telnet(bool showSeparators) =>
		Encoding.UTF8.GetString(new MarkupOutputRenderer().Render(Markup, Connection(showSeparators)).Data).TrimEnd('\r', '\n');

	[Test]
	public async Task ASeparatorIsSentAsItsText()
		=> await Assert.That(Telnet(showSeparators: false)).IsEqualTo("a|b|c");

	[Test]
	public async Task ShowSeparators_DrawsEachInReverseVideo()
		=> await Assert.That(Telnet(showSeparators: true)).IsEqualTo("a|b\e[7m|\e[0mc");

	[Test]
	public async Task ShowSeparators_MarksThemForThePortal()
	{
		var envelope = Encoding.UTF8.GetString(new MarkupOutputRenderer().Render(Markup, Connection(true, "websocket")).Data);
		var sent = MarkupTextSerializer.Deserialize(JsonDocument.Parse(envelope).RootElement.GetProperty("data").GetString()!);

		var inverted = sent.Runs.Where(run => run.Markups.OfType<AnsiMarkup>().Any(markup => markup.Style.Inverted)).ToList();
		await Assert.That(inverted.Select(run => (run.Start, run.Length))).IsEquivalentTo([(3, 1)]);
	}
}
