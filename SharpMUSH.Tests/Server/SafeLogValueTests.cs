using SharpMUSH.Server.Logging;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// The mitigation behind the CodeQL "log entries created from user input" alerts on the softcode
/// HTTP route: routing percent-decodes route values, so a request path is a string the caller
/// controls character for character, newlines included.
/// </summary>
public class SafeLogValueTests
{
	[Test]
	public async Task AForgedLineBreakCannotSurviveIntoTheLog()
	{
		// What "/http/foo%0AWARN:+forged+entry" decodes to by the time it reaches a log call.
		var forged = SafeLogValue.OneLine("/foo\nWARN: forged entry");

		await Assert.That(forged).DoesNotContain("\n");
		await Assert.That(forged).IsEqualTo("/foo\uFFFDWARN: forged entry");
	}

	[Test]
	[Arguments("\r")]
	[Arguments("\n")]
	[Arguments("\r\n")]
	[Arguments("\u0000")]
	[Arguments("\u001b[2J")]
	[Arguments("\u007f")]
	[Arguments("\u0085")]
	public async Task EveryControlCharacterIsReplaced(string control)
	{
		// Not just CRLF: a NUL, an ANSI escape, DEL or a C1 character can each garble or forge an
		// entry depending on the sink, so the guard is char.IsControl rather than a line-break list.
		var sanitized = SafeLogValue.OneLine($"/path{control}tail");

		await Assert.That(sanitized.Any(char.IsControl)).IsFalse();
		await Assert.That(sanitized).StartsWith("/path");
		await Assert.That(sanitized).EndsWith("tail");
	}

	[Test]
	public async Task OrdinaryTextIsLeftAlone()
	{
		// The entry still has to say what was actually requested, including non-ASCII paths.
		const string path = "/characters?name=Joe+Smith&tag=caf\u00e9";

		await Assert.That(SafeLogValue.OneLine(path)).IsEqualTo(path);
		await Assert.That(SafeLogValue.OneLine("/\u754c/\u8def\u5f84")).IsEqualTo("/\u754c/\u8def\u5f84");
	}

	[Test]
	public async Task NothingIsSafeToLogAsEmpty()
	{
		await Assert.That(SafeLogValue.OneLine(null)).IsEqualTo(string.Empty);
		await Assert.That(SafeLogValue.OneLine(string.Empty)).IsEqualTo(string.Empty);
	}
}
