using System.Text;
using MarkupString;
using MarkupString.Layout;
using SharpMUSH.Configuration;
using SharpMUSH.Library.Utilities;
using SharpMUSH.RenderingWorker.Services;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A connection not written in UTF-8 is sent each character it cannot show as the nearest one it can, the
/// game's <c>ascii_translations</c> first, and in the character set it reads.
/// </summary>
public class AsciiOutputTests
{
	private static readonly ProtocolCapabilities Ascii = new(SupportsUtf8: false);

	private static string Render(MarkupText text, ProtocolCapabilities capabilities,
		IReadOnlyDictionary<string, string>? translations = null)
	{
		var context = new RenderContext("telnet", capabilities, null, AsciiTranslations: translations);
		var rendered = new MarkupOutputRenderer().Render(MarkupTextSerializer.Serialize(text), context);
		var bytes = rendered.ApplyOutputTransform ? OutputTransformService.Transform(rendered.Data, capabilities) : rendered.Data;
		// What the client reads, in the character set it reads.
		return (capabilities.OutputCharset == TerminalCharsets.Latin1 ? Encoding.Latin1 : Encoding.UTF8)
			.GetString(bytes).Replace("\r\n", "\n").TrimEnd('\n');
	}

	[Test]
	public async Task AnAsciiClient_GetsTheNearestCharacters()
		=> await Assert.That(Render(MarkupText.Plain("Wren · Scene 3 — “café”"), Ascii))
			.IsEqualTo("Wren * Scene 3 - \"cafe\"");

	[Test]
	public async Task AUtf8Client_GetsTheTextAsItIs()
		=> await Assert.That(Render(MarkupText.Plain("Wren · Scene 3"), new ProtocolCapabilities()))
			.IsEqualTo("Wren · Scene 3");

	[Test]
	public async Task ALatin1Client_KeepsWhatLatin1Has()
		=> await Assert.That(Render(MarkupText.Plain("Wren · café — x"), new ProtocolCapabilities(Charset: "ISO-8859-1")))
			.IsEqualTo("Wren · café - x");

	[Test]
	public async Task TheGamesTranslations_ComeFirst()
		=> await Assert.That(Render(MarkupText.Plain("Wren·Scene…"), Ascii, new Dictionary<string, string> { ["·"] = " - ", ["…"] = "..." }))
			.IsEqualTo("Wren - Scene...");

	[Test]
	public async Task AnEntryThatCannotWork_IsLeftOutAndTheRestKept()
		=> await Assert.That(Render(MarkupText.Plain("a·b…"), Ascii, new Dictionary<string, string> { ["ab"] = "x", ["…"] = "..." }))
			.IsEqualTo("a*b...");

	/// <summary>A layout is lined up after the fold, so a longer stand-in in a title keeps the box square.</summary>
	[Test]
	public async Task ALayout_StaysLinedUpAroundALongerStandIn()
	{
		var box = BlockLayout.Build(
			new Frame(new TextBlock(MarkupText.Plain("Tea · garden"))) { Border = BorderStyle.Single, Title = MarkupText.Plain("Wren · 3") },
			24, fluid: false);

		var lines = Render(box, Ascii, new Dictionary<string, string> { ["·"] = " - " }).Split('\n');

		await Assert.That(lines[0]).Contains("Wren  -  3");
		await Assert.That(lines[1]).Contains("Tea  -  garden");
		await Assert.That(lines.Select(line => line.Length).Distinct().Count()).IsEqualTo(1);
		await Assert.That(lines.All(line => line.All(char.IsAscii))).IsTrue();
	}

	[Test]
	public async Task StripAccents_SendsAsciiWhateverTheClientSaid()
	{
		var capabilities = new ProtocolCapabilities(Charset: "UTF-8", Pins: new TerminalPins(StripAccents: true));
		await Assert.That(capabilities.OutputCharset).IsEqualTo(TerminalCharsets.Ascii);
		await Assert.That(Render(MarkupText.Plain("café"), capabilities)).IsEqualTo("cafe");
	}

	[Test]
	public async Task AKnownTerminal_IsSentUtf8WithoutSayingSo()
	{
		await Assert.That(new ProtocolCapabilities(SupportsUtf8: false, TerminalTypes: ["xterm-kitty"]).Utf8).IsTrue();
		await Assert.That(new ProtocolCapabilities(SupportsUtf8: false, TerminalTypes: ["xterm-256color"]).Utf8).IsFalse();
	}

	[Test]
	[Arguments(null, null, true, TerminalCharsets.Utf8)]
	[Arguments(null, null, false, TerminalCharsets.Ascii)]
	[Arguments(null, "UTF-8", false, TerminalCharsets.Utf8)]
	[Arguments(null, "ISO-8859-1", true, TerminalCharsets.Latin1)]
	[Arguments("ascii", "UTF-8", true, TerminalCharsets.Ascii)]
	[Arguments("utf-8", null, false, TerminalCharsets.Utf8)]
	public async Task TheCharset_IsThePinThenTheNegotiationThenTheClientsClaim(
		string? pin, string? negotiated, bool claimsUtf8, string expected)
		=> await Assert.That(TerminalCharsets.Resolve(new TerminalPins(Charset: pin), negotiated, claimsUtf8)).IsEqualTo(expected);

	[Test]
	[Arguments("·", " - ", true)]
	[Arguments("©", "", true)]
	[Arguments("a", "x", false)]
	[Arguments("ab", "x", false)]
	[Arguments("·", "é", false)]
	[Arguments("·", "\n", false)]
	public async Task AnEntry_IsOneCharacterOutsideAsciiAndPlainAsciiText(string character, string text, bool works)
		=> await Assert.That(AsciiTranslations.Problem(character, text) is null).IsEqualTo(works);

	[Test]
	public async Task TheFingerprint_IgnoresOrder()
		=> await Assert.That(AsciiTranslations.Fingerprint(new Dictionary<string, string> { ["·"] = "*", ["…"] = "..." }))
			.IsEqualTo(AsciiTranslations.Fingerprint(new Dictionary<string, string> { ["…"] = "...", ["·"] = "*" }));
}
