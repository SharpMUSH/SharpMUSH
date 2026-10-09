using SharpMUSH.Library.Utilities;
using SharpMUSH.RenderingWorker.Services;
using SharpMUSH.SocketServer.Models;
using System.Text;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// The transform writes output in the client's character set. Colour is the renderer's: it writes each colour
/// at the depth the connection can display (<see cref="Services.MarkupOutputRendererTests"/>).
/// </summary>
public class OutputTransformServiceTests
{
	private readonly OutputTransformService _service = new();

	[Test]
	public async Task TransformAsync_ConvertsToAscii_WhenAsciiCharset()
	{
		var input = "Hello © World"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsUtf8: false, Charset: "ASCII");

		var result = await _service.TransformAsync(input, capabilities);

		await Assert.That(Encoding.ASCII.GetString(result)).IsEqualTo("Hello ? World");
	}

	[Test]
	public async Task Transform_SendsTheNearestCharacter_ForOutputSentAsItIs()
	{
		var input = "Wren · Scene — café"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsUtf8: false);

		await Assert.That(Encoding.ASCII.GetString(OutputTransformService.Transform(input, capabilities)))
			.IsEqualTo("Wren * Scene - cafe");
		await Assert.That(Encoding.ASCII.GetString(OutputTransformService.Transform(input, capabilities, new Dictionary<string, string> { ["·"] = "+" })))
			.IsEqualTo("Wren + Scene - cafe");
	}

	[Test]
	public async Task TransformAsync_ConvertsToLatin1_WhenLatin1Charset()
	{
		var input = "café ☃"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsUtf8: false, Charset: "ISO-8859-1");

		var result = await _service.TransformAsync(input, capabilities);

		await Assert.That(result).IsEquivalentTo(Encoding.Latin1.GetBytes("café ?"));
	}

	[Test]
	public async Task TransformAsync_PreservesUtf8_WhenUtf8Charset()
	{
		var input = "Hello © World 🎮"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsUtf8: true, Charset: "UTF-8");

		var result = await _service.TransformAsync(input, capabilities);

		await Assert.That(Encoding.UTF8.GetString(result)).IsEqualTo("Hello © World 🎮");
	}

	/// <summary>
	/// Output bound for UTF-8 goes out byte for byte, escapes and all: the renderer already wrote it for this
	/// client, and a decode/re-encode round trip must never touch a valid multi-byte sequence.
	/// </summary>
	[Test]
	public async Task TransformAsync_PassesUtf8Through_Unchanged()
	{
		var input = "\x1b[1;38;2;255;0;0mA snowman ☃\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsAnsi: false, ColorStylePin: ColorStyles.Plain);

		var result = await _service.TransformAsync(input, capabilities);

		await Assert.That(result).IsEquivalentTo(input);
	}

	[Test]
	public async Task TransformAsync_HandlesInvalidUtf8Gracefully()
	{
		var input = new byte[] { 0xFF, 0xFE, 0xFD };
		var capabilities = new ProtocolCapabilities(Charset: "ASCII");

		var result = await _service.TransformAsync(input, capabilities);

		// Each invalid byte decodes as U+FFFD, which ASCII cannot hold.
		await Assert.That(Encoding.ASCII.GetString(result)).IsEqualTo("???");
	}
}
