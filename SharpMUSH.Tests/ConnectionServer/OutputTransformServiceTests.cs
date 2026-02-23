using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.ConnectionServer.Services;
using System.Text;

namespace SharpMUSH.Tests.ConnectionServer;

public class OutputTransformServiceTests
{
	private readonly OutputTransformService _service;

	public OutputTransformServiceTests()
	{
		_service = new OutputTransformService(NullLogger<OutputTransformService>.Instance);
	}

	[Test]
	public async Task TransformAsync_NoTransformation_WhenAnsiEnabledAndSupported()
	{
		// Arrange
		var input = "\x1b[31mRed text\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsAnsi: true);
		var preferences = new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: true);

		// Act
		var result = await _service.TransformAsync(input, capabilities, preferences);

		// Assert
		var resultText = Encoding.UTF8.GetString(result);
		await Assert.That(resultText).IsEqualTo("\x1b[31mRed text\x1b[0m");
	}

	[Test]
	public async Task TransformAsync_StripsAnsi_WhenAnsiDisabledInPreferences()
	{
		// Arrange
		var input = "\x1b[31mRed text\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsAnsi: true);
		var preferences = new PlayerOutputPreferences(AnsiEnabled: false);

		// Act
		var result = await _service.TransformAsync(input, capabilities, preferences);

		// Assert
		var resultText = Encoding.UTF8.GetString(result);
		await Assert.That(resultText).IsEqualTo("Red text");
	}

	[Test]
	public async Task TransformAsync_StripsAnsi_WhenColorDisabledInPreferences()
	{
		// Arrange
		var input = "\x1b[31mRed text\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsAnsi: true);
		var preferences = new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: false);

		// Act
		var result = await _service.TransformAsync(input, capabilities, preferences);

		// Assert
		var resultText = Encoding.UTF8.GetString(result);
		await Assert.That(resultText).IsEqualTo("Red text");
	}

	[Test]
	public async Task TransformAsync_StripsAnsi_WhenClientDoesNotSupportAnsi()
	{
		// Arrange
		var input = "\x1b[31mRed text\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsAnsi: false);
		var preferences = new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: true);

		// Act
		var result = await _service.TransformAsync(input, capabilities, preferences);

		// Assert
		var resultText = Encoding.UTF8.GetString(result);
		await Assert.That(resultText).IsEqualTo("Red text");
	}

	[Test]
	public async Task TransformAsync_StripsAnsi_WhenNoPreferences()
	{
		// Arrange
		var input = "\x1b[31mRed text\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsAnsi: false);

		// Act
		var result = await _service.TransformAsync(input, capabilities, null);

		// Assert
		var resultText = Encoding.UTF8.GetString(result);
		await Assert.That(resultText).IsEqualTo("Red text");
	}

	[Test]
	public async Task TransformAsync_DowngradesXterm256_WhenNotSupported()
	{
		// Arrange
		var input = "\x1b[38;5;196mBright red\x1b[0m"u8.ToArray(); // 256-color red (196)
		var capabilities = new ProtocolCapabilities(SupportsAnsi: true, SupportsXterm256: false);
		var preferences = new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: true, Xterm256Enabled: false);

		// Act
		var result = await _service.TransformAsync(input, capabilities, preferences);

		// Assert
		var resultText = Encoding.UTF8.GetString(result);
		// 196 in 256-color palette should be downgraded to a 16-color code
		// The regex replaces 38;5;N with 3X where X is the mapped color
		await Assert.That(resultText).Contains("\x1b[3"); // Should have a 16-color foreground code
		await Assert.That(resultText).Contains("Bright red"); // Text should be preserved
		await Assert.That(resultText).DoesNotContain("38;5;196"); // 256-color code should be removed
	}

	[Test]
	public async Task TransformAsync_PreservesXterm256_WhenSupported()
	{
		// Arrange
		var input = "\x1b[38;5;196mBright red\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsAnsi: true, SupportsXterm256: true);
		var preferences = new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: true, Xterm256Enabled: true);

		// Act
		var result = await _service.TransformAsync(input, capabilities, preferences);

		// Assert
		var resultText = Encoding.UTF8.GetString(result);
		await Assert.That(resultText).Contains("38;5;196");
	}

	[Test]
	public async Task TransformAsync_ConvertsToAscii_WhenAsciiCharset()
	{
		// Arrange
		var input = "Hello © World"u8.ToArray(); // Contains UTF-8 copyright symbol
		var capabilities = new ProtocolCapabilities(SupportsUtf8: false, Charset: "ASCII");

		// Act
		var result = await _service.TransformAsync(input, capabilities, null);

		// Assert
		var resultText = Encoding.ASCII.GetString(result);
		// ASCII encoding will replace © with ?
		await Assert.That(resultText).Contains("Hello");
		await Assert.That(resultText).Contains("World");
		await Assert.That(resultText).DoesNotContain("©");
	}

	[Test]
	public async Task TransformAsync_PreservesUtf8_WhenUtf8Charset()
	{
		// Arrange
		var input = "Hello © World 🎮"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsUtf8: true, Charset: "UTF-8");

		// Act
		var result = await _service.TransformAsync(input, capabilities, null);

		// Assert
		var resultText = Encoding.UTF8.GetString(result);
		await Assert.That(resultText).IsEqualTo("Hello © World 🎮");
	}

	[Test]
	public async Task TransformAsync_HandlesComplexAnsi_StripsAll()
	{
		// Arrange
		var input = "\x1b[1m\x1b[31mBold Red\x1b[0m \x1b[4m\x1b[32mUnderline Green\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsAnsi: false);

		// Act
		var result = await _service.TransformAsync(input, capabilities, null);

		// Assert
		var resultText = Encoding.UTF8.GetString(result);
		await Assert.That(resultText).IsEqualTo("Bold Red Underline Green");
	}

	[Test]
	public async Task TransformAsync_HandlesInvalidUtf8Gracefully()
	{
		// Arrange
		var input = new byte[] { 0xFF, 0xFE, 0xFD }; // Invalid UTF-8
		var capabilities = new ProtocolCapabilities();

		// Act
		var result = await _service.TransformAsync(input, capabilities, null);

		// Assert - service should handle invalid UTF-8 gracefully by using replacement characters
		// The exact output will be UTF-8 replacement characters (U+FFFD = EF BF BD in UTF-8)
		var resultText = Encoding.UTF8.GetString(result);
		await Assert.That(resultText).IsNotEmpty();
		// UTF-8 encoding converts invalid bytes to replacement character
		await Assert.That(resultText).Contains("\uFFFD"); // Replacement character
	}
}
