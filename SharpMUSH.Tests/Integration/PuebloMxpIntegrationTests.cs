using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Integration tests for Pueblo handshake and MXP telnet negotiation.
/// Reuses the <see cref="TelnetIntegrationFixture"/> which starts both
/// the SharpMUSH Server and ConnectionServer with shared NATS.
///
/// Run with: --treenode-filter "/*/*/PuebloMxpIntegrationTests/*"
/// </summary>
[Category("NeedsSetup")]
[Explicit]
[NotInParallel]
public class PuebloMxpIntegrationTests
{
	private const int ReceiveTimeoutMs = 20_000;
	private const int PollingIntervalMs = 200;

	[ClassDataSource<TelnetIntegrationFixture>(Shared = SharedType.PerClass)]
	public required TelnetIntegrationFixture Fixture { get; init; }

	/// <summary>
	/// Verifies that after connecting, the server sends the Pueblo hello string
	/// ("This world is Pueblo 1.10 Enhanced.") before the login screen.
	/// </summary>
	[Test]
	[Timeout(60_000)]
	public async Task PuebloHandshake_ServerSendsPuebloHello(CancellationToken cancellationToken)
	{
		using var client = new TcpClient();
		await client.ConnectAsync(IPAddress.Loopback, Fixture.TelnetPort);
		client.ReceiveTimeout = ReceiveTimeoutMs;

		await using var stream = client.GetStream();
		var received = await ReadUntilAsync(stream,
			s => s.Contains("Pueblo 1.10 Enhanced"), cancellationToken);

		await Assert.That(received).Contains("Pueblo 1.10 Enhanced")
			.Because("Server should send Pueblo hello string on connect when Pueblo is enabled");
	}

	/// <summary>
	/// Verifies the full Pueblo handshake: client sends PUEBLOCLIENT response,
	/// logs in, and receives HTML-formatted output (e.g. &lt;send&gt; tags for exits).
	/// </summary>
	[Test]
	[Timeout(120_000)]
	public async Task PuebloHandshake_ClientResponds_OutputSwitchesToHtml(CancellationToken cancellationToken)
	{
		using var client = new TcpClient();
		await client.ConnectAsync(IPAddress.Loopback, Fixture.TelnetPort);
		client.ReceiveTimeout = ReceiveTimeoutMs;

		await using var stream = client.GetStream();

		var hello = await ReadUntilAsync(stream,
			s => s.Contains("Pueblo 1.10 Enhanced"), cancellationToken);
		await Assert.That(hello).Contains("Pueblo 1.10 Enhanced");

		await SendLineAsync(stream, "PUEBLOCLIENT 2.01", cancellationToken);

		await Task.Delay(500, cancellationToken);

		var loginScreen = await ReadUntilAsync(stream,
			s => s.Contains("Welcome to SharpMUSH"), cancellationToken);
		await Assert.That(loginScreen).Contains("Welcome to SharpMUSH");

		await SendLineAsync(stream, "connect God", cancellationToken);

		var postLogin = await ReadUntilAsync(stream,
			s => s.Contains("Room Zero"), cancellationToken);

		await Assert.That(postLogin).Contains("Room Zero")
			.Because("After login, Pueblo client should see Room Zero");
		await Assert.That(postLogin).Contains("<send")
			.Because("Pueblo output should switch to HTML/MXP-style send tags after the handshake");
	}

	/// <summary>
	/// Verifies that without a PUEBLOCLIENT response, the server sends
	/// standard ANSI output (no HTML tags in the room description).
	/// </summary>
	[Test]
	[Timeout(120_000)]
	public async Task NoPuebloHandshake_OutputRemainsAnsi(CancellationToken cancellationToken)
	{
		using var client = new TcpClient();
		await client.ConnectAsync(IPAddress.Loopback, Fixture.TelnetPort);
		client.ReceiveTimeout = ReceiveTimeoutMs;

		await using var stream = client.GetStream();

		var loginScreen = await ReadUntilAsync(stream,
			s => s.Contains("Welcome to SharpMUSH"), cancellationToken);
		await Assert.That(loginScreen).Contains("Welcome to SharpMUSH");

		await SendLineAsync(stream, "connect God", cancellationToken);

		var postLogin = await ReadUntilAsync(stream,
			s => s.Contains("Room Zero"), cancellationToken);

		await Assert.That(postLogin).Contains("Room Zero")
			.Because("Room Zero should appear in ANSI mode");

		await Assert.That(postLogin).DoesNotContain("<send")
			.Because("ANSI output must not contain HTML send tags");
		await Assert.That(postLogin).DoesNotContain("<FONT")
			.Because("ANSI output must not contain HTML FONT tags");
	}

	/// <summary>
	/// Verifies that the server sends IAC WILL MXP (255 251 91) during
	/// initial telnet negotiation.
	/// </summary>
	[Test]
	[Timeout(60_000)]
	public async Task MxpNegotiation_ServerSendsWillMxp(CancellationToken cancellationToken)
	{
		using var client = new TcpClient();
		await client.ConnectAsync(IPAddress.Loopback, Fixture.TelnetPort);
		client.ReceiveTimeout = ReceiveTimeoutMs;

		await using var stream = client.GetStream();

		// Read raw bytes looking for IAC WILL MXP (0xFF 0xFB 0x5B)
		var rawReceived = await ReadRawUntilAsync(stream,
			bytes => ContainsSequence(bytes, [0xFF, 0xFB, 0x5B]),
			cancellationToken,
			timeoutMs: 10_000);

		await Assert.That(ContainsSequence(rawReceived, [0xFF, 0xFB, 0x5B])).IsTrue()
			.Because("Server should send IAC WILL MXP (telnet option 91) during negotiation");
	}

	/// <summary>
	/// Verifies that when the client responds with IAC DO MXP, and then logs in,
	/// the output includes MXP open line prefixes (ESC[0z).
	/// </summary>
	[Test]
	[Timeout(120_000)]
	public async Task MxpNegotiation_ClientAccepts_OutputIncludesMxpTags(CancellationToken cancellationToken)
	{
		using var client = new TcpClient();
		await client.ConnectAsync(IPAddress.Loopback, Fixture.TelnetPort);
		client.ReceiveTimeout = ReceiveTimeoutMs;

		await using var stream = client.GetStream();

		var rawInit = await ReadRawUntilAsync(stream,
			bytes => ContainsSequence(bytes, [0xFF, 0xFB, 0x5B]),
			cancellationToken,
			timeoutMs: 10_000);

		await Assert.That(ContainsSequence(rawInit, [0xFF, 0xFB, 0x5B])).IsTrue();

		// Respond with IAC DO MXP (0xFF 0xFD 0x5B) to accept MXP
		await stream.WriteAsync(new byte[] { 0xFF, 0xFD, 0x5B }, cancellationToken);
		await stream.FlushAsync(cancellationToken);

		await Task.Delay(500, cancellationToken);

		var loginScreen = await ReadUntilAsync(stream,
			s => s.Contains("Welcome to SharpMUSH"), cancellationToken);
		await Assert.That(loginScreen).Contains("Welcome to SharpMUSH");

		await SendLineAsync(stream, "connect God", cancellationToken);

		var postLogin = await ReadUntilAsync(stream,
			s => s.Contains("Room Zero"), cancellationToken);

		await Assert.That(postLogin).Contains("Room Zero")
			.Because("After login with MXP, should see Room Zero");

		await Assert.That(postLogin).Contains("\x1b[0z")
			.Because("MXP output lines should be prefixed with ESC[0z open mode");
		await Assert.That(postLogin).Contains("<send")
			.Because("MXP output should still include send tags when the client accepts MXP");
	}

	/// <summary>
	/// Verifies that when the client refuses MXP (IAC DONT MXP),
	/// the server falls back to ANSI output without MXP tags.
	/// </summary>
	[Test]
	[Timeout(120_000)]
	public async Task MxpNegotiation_ClientRefuses_OutputRemainsAnsi(CancellationToken cancellationToken)
	{
		using var client = new TcpClient();
		await client.ConnectAsync(IPAddress.Loopback, Fixture.TelnetPort);
		client.ReceiveTimeout = ReceiveTimeoutMs;

		await using var stream = client.GetStream();

		// Wait for IAC WILL MXP (drain initial negotiation bytes)
		await ReadRawUntilAsync(stream,
			bytes => ContainsSequence(bytes, [0xFF, 0xFB, 0x5B]),
			cancellationToken,
			timeoutMs: 10_000);

		// Respond with IAC DONT MXP (0xFF 0xFE 0x5B) to refuse
		await stream.WriteAsync(new byte[] { 0xFF, 0xFE, 0x5B }, cancellationToken);
		await stream.FlushAsync(cancellationToken);

		var loginScreen = await ReadUntilAsync(stream,
			s => s.Contains("Welcome to SharpMUSH"), cancellationToken);

		await SendLineAsync(stream, "connect God", cancellationToken);

		var postLogin = await ReadUntilAsync(stream,
			s => s.Contains("Room Zero"), cancellationToken);

		await Assert.That(postLogin).Contains("Room Zero");

		await Assert.That(postLogin).DoesNotContain("\x1b[0z")
			.Because("ANSI output should not contain MXP line prefixes");
		await Assert.That(postLogin).DoesNotContain("<SEND")
			.Because("ANSI output should not contain MXP SEND tags");
	}

	private static async Task<string> ReadUntilAsync(
		NetworkStream stream,
		Func<string, bool> stopCondition,
		CancellationToken cancellationToken)
	{
		var buffer = new byte[4096];
		var received = new StringBuilder();

		while (!cancellationToken.IsCancellationRequested)
		{
			if (stream.DataAvailable)
			{
				var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
				if (bytesRead > 0)
				{
					received.Append(StripTelnetControlBytes(buffer, bytesRead));
					if (stopCondition(received.ToString()))
						break;
				}
			}
			else
			{
				try { await Task.Delay(PollingIntervalMs, cancellationToken); }
				catch (OperationCanceledException) { break; }
			}
		}

		return received.ToString();
	}

	/// <summary>
	/// Reads raw bytes (without stripping telnet control sequences) until
	/// the stop condition is met or the timeout expires.
	/// </summary>
	private static async Task<byte[]> ReadRawUntilAsync(
		NetworkStream stream,
		Func<byte[], bool> stopCondition,
		CancellationToken cancellationToken,
		int timeoutMs = 20_000)
	{
		var buffer = new byte[4096];
		var received = new List<byte>();
		using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cts.CancelAfter(timeoutMs);

		try
		{
			while (!cts.Token.IsCancellationRequested)
			{
				if (stream.DataAvailable)
				{
					var bytesRead = await stream.ReadAsync(buffer, cts.Token);
					if (bytesRead > 0)
					{
						received.AddRange(buffer.AsSpan(0, bytesRead).ToArray());
						if (stopCondition(received.ToArray()))
							break;
					}
				}
				else
				{
					await Task.Delay(PollingIntervalMs, cts.Token);
				}
			}
		}
		catch (OperationCanceledException) { }

		return received.ToArray();
	}

	private static async Task SendLineAsync(NetworkStream stream, string line, CancellationToken cancellationToken)
	{
		var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
		await stream.WriteAsync(bytes, cancellationToken);
		await stream.FlushAsync(cancellationToken);
	}

	/// <summary>
	/// Checks whether the byte array contains the given subsequence.
	/// </summary>
	private static bool ContainsSequence(byte[] data, byte[] sequence)
	{
		for (int i = 0; i <= data.Length - sequence.Length; i++)
		{
			bool match = true;
			for (int j = 0; j < sequence.Length; j++)
			{
				if (data[i + j] != sequence[j])
				{
					match = false;
					break;
				}
			}
			if (match) return true;
		}
		return false;
	}

	private static string StripTelnetControlBytes(byte[] data, int length)
	{
		var result = new List<byte>(length);
		for (var i = 0; i < length; i++)
		{
			if (data[i] != 0xFF)
			{
				result.Add(data[i]);
				continue;
			}

			if (i + 1 >= length) break;
			var cmd = data[i + 1];

			if (cmd is 0xFB or 0xFC or 0xFD or 0xFE)
			{
				i += 2;
			}
			else if (cmd == 0xFA) // SB subnegotiation — skip until SE (0xF0)
			{
				i += 2;
				while (i < length && data[i] != 0xF0) i++;
			}
			else if (cmd == 0xFF)
			{
				result.Add(0xFF);
				i++;
			}
			else
			{
				i++;
			}
		}

		return Encoding.UTF8.GetString(result.ToArray());
	}
}
