using SharpMUSH.Library.Definitions;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Internal;

/// <summary>
/// Contract tests for the <c>#-1 EXCEPTION: {json}</c> payload builder, in particular the
/// disclosure rule: a mortal's payload is assembled from an allowlist, so it cannot carry a file
/// path, a stack frame, or a connection string no matter what the exception contains.
/// </summary>
public class ExceptionReportTests
{
	/// <summary>
	/// An exception deliberately stuffed with everything a mortal must never see: an absolute path,
	/// a Windows path, a connection string with credentials, and (via <see cref="Thrown"/>) a real
	/// stack trace with frame markers.
	/// </summary>
	private const string LeakyMessage =
		"Could not open /opt/sharpmush/game/data/secrets.json or C:\\SharpMUSH\\db.dat using " +
		"Server=db.internal;Database=mush;User Id=sa;Password=hunter2;";

	private static Exception Thrown()
	{
		try
		{
			throw new InvalidOperationException(LeakyMessage,
				new IOException("/var/lib/sharpmush/arangodb.sock is unavailable"));
		}
		catch (InvalidOperationException caught)
		{
			return caught;
		}
	}

	[Test]
	public async Task FormatUsesTheEstablishedErrorPrefix()
	{
		var formatted = ExceptionReport.Format(Thrown(), "@CHANNEL", "abc123abc123", privileged: false);

		await Assert.That(formatted).StartsWith("#-1 EXCEPTION: {");
	}

	[Test]
	public async Task PayloadIsValidJson()
	{
		var payload = ExceptionReport.Payload(Thrown(), "@CHANNEL", "abc123abc123", privileged: true);

		using var document = JsonDocument.Parse(payload);

		await Assert.That(document.RootElement.ValueKind).IsEqualTo(JsonValueKind.Object);
		await Assert.That(document.RootElement.GetProperty("id").GetString()).IsEqualTo("abc123abc123");
		await Assert.That(document.RootElement.GetProperty("command").GetString()).IsEqualTo("@CHANNEL");
		await Assert.That(document.RootElement.GetProperty("type").GetString())
			.IsEqualTo(nameof(InvalidOperationException));
	}

	[Test]
	public async Task PayloadIsASingleLineEvenWhenTheMessageIsNot()
	{
		var multiline = new Exception("first line\nsecond line\r\nthird line");

		var payload = ExceptionReport.Payload(multiline, "@SWITCH", "abc123abc123", privileged: true);

		await Assert.That(payload).DoesNotContain("\n");
		await Assert.That(payload).DoesNotContain("\r");
	}

	[Test]
	public async Task MortalPayloadLeaksNoInternals()
	{
		var payload = ExceptionReport.Payload(Thrown(), "@CHANNEL", "abc123abc123", privileged: false);

		// The exception message and every fragment of it stay server-side.
		await Assert.That(payload).DoesNotContain("secrets.json");
		await Assert.That(payload).DoesNotContain("/opt/sharpmush");
		await Assert.That(payload).DoesNotContain("C:\\SharpMUSH");
		await Assert.That(payload).DoesNotContain("Password=");
		await Assert.That(payload).DoesNotContain("arangodb.sock");
		await Assert.That(payload).DoesNotContain(nameof(IOException));

		// No stack frames: neither the "at Namespace.Type.Method" form nor a source-line reference.
		await Assert.That(payload).DoesNotContain("   at ");
		await Assert.That(payload).DoesNotContain(".cs:line");
		await Assert.That(payload).DoesNotContain("ExceptionReportTests");

		// Nothing path-like at all: no directory separators, no drive letter, no file extension.
		// JSON escapes a backslash as \\, so a raw '\' cannot appear except as an escape - and the
		// allowlisted fields (hex id, command word, CLR type name) never need one.
		await Assert.That(Regex.IsMatch(payload, @"[/\\]")).IsFalse();
		await Assert.That(Regex.IsMatch(payload, @"\.(cs|json|dat|dll|sock|log)\b")).IsFalse();

		// And only the three allowlisted keys are present.
		using var document = JsonDocument.Parse(payload);
		var keys = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
		await Assert.That(keys).IsEquivalentTo(new[] { "id", "command", "type" });
	}

	[Test]
	public async Task PrivilegedPayloadCarriesMessageAndInnerChain()
	{
		var payload = ExceptionReport.Payload(Thrown(), "@CHANNEL", "abc123abc123", privileged: true);

		using var document = JsonDocument.Parse(payload);

		await Assert.That(document.RootElement.GetProperty("message").GetString()).IsEqualTo(LeakyMessage);

		var inner = document.RootElement.GetProperty("inner");
		await Assert.That(inner.GetArrayLength()).IsEqualTo(1);
		await Assert.That(inner[0].GetProperty("type").GetString()).IsEqualTo(nameof(IOException));
		await Assert.That(inner[0].GetProperty("message").GetString())
			.IsEqualTo("/var/lib/sharpmush/arangodb.sock is unavailable");
	}

	[Test]
	public async Task PrivilegedPayloadNeverCarriesAStackTrace()
	{
		var payload = ExceptionReport.Payload(Thrown(), "@CHANNEL", "abc123abc123", privileged: true);

		// Stack frames are unreadable on a MUSH line; the server log keeps them, reachable by id.
		await Assert.That(payload).DoesNotContain("   at ");
		await Assert.That(payload).DoesNotContain(".cs:line");
		await Assert.That(payload).DoesNotContain("stack");
	}

	[Test]
	public async Task LongMessagesAreClamped()
	{
		var payload = ExceptionReport.Payload(new Exception(new string('x', 5000)), "@SWITCH",
			"abc123abc123", privileged: true);

		using var document = JsonDocument.Parse(payload);
		var message = document.RootElement.GetProperty("message").GetString()!;

		await Assert.That(message.Length).IsEqualTo(503);
		await Assert.That(message).EndsWith("...");
	}

	[Test]
	public async Task CommandIsOmittedWhenUnknown()
	{
		var payload = ExceptionReport.Payload(new Exception("boom"), null, "abc123abc123", privileged: false);

		using var document = JsonDocument.Parse(payload);

		await Assert.That(document.RootElement.TryGetProperty("command", out _)).IsFalse();
		await Assert.That(document.RootElement.GetProperty("id").GetString()).IsEqualTo("abc123abc123");
	}

	[Test]
	public async Task CorrelationIdsAreUniqueAndOpaque()
	{
		var ids = Enumerable.Range(0, 1000).Select(_ => ExceptionReport.NewCorrelationId()).ToArray();

		await Assert.That(ids.Distinct().Count()).IsEqualTo(1000);
		await Assert.That(ids.All(id => id.Length == 12)).IsTrue();
		await Assert.That(ids.All(id => Regex.IsMatch(id, "^[0-9a-f]{12}$"))).IsTrue();
	}
}
