using System.Net;
using System.Net.Sockets;
using System.Text;
using MarkupString;
using MarkupString.Html;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.Integration;

/// <summary>Real TCP negotiation and known markup through NATS, independent of room contents.</summary>
[Category("NeedsSetup")]
[Explicit]
[NotInParallel]
public class PuebloMxpIntegrationTests
{
	private const int ReceiveTimeoutMs = 20_000;

	[ClassDataSource<TelnetIntegrationFixture>(Shared = SharedType.PerClass)]
	public required TelnetIntegrationFixture Fixture { get; init; }

	[Test]
	[Timeout(60_000)]
	public async Task PuebloHandshake_ServerSendsPuebloHello(CancellationToken cancellationToken)
	{
		await using var client = await ConnectAsync(cancellationToken);
		var received = await client.ReadUntilAsync(s => s.Contains("Pueblo 1.10 Enhanced"), cancellationToken);
		await Assert.That(received).Contains("Pueblo 1.10 Enhanced");
	}

	[Test]
	[Timeout(60_000)]
	public async Task PuebloHandshake_ClientResponds_OutputSwitchesToHtml(CancellationToken cancellationToken)
	{
		await using var client = await ConnectAsync(cancellationToken);
		await client.ReadUntilAsync(s => s.Contains("Pueblo 1.10 Enhanced"), cancellationToken);
		await client.SendLineAsync("PUEBLOCLIENT 2.01", cancellationToken);
		await WaitForFormatAsync(client.Handle, OutputFormat.Pueblo, cancellationToken);

		var output = await PublishKnownMarkupAsync(client, cancellationToken);
		await Assert.That(output.ToUpperInvariant()).Contains("<SEND");
		await Assert.That(output.ToUpperInvariant()).Contains("HREF=\"LOOK\"");
	}

	[Test]
	[Timeout(60_000)]
	public async Task PuebloHandshake_ArrivingLate_StillSwitchesToHtml(CancellationToken cancellationToken)
	{
		await using var client = await ConnectAsync(cancellationToken);
		// WHO must be processed first, proving PUEBLOCLIENT is not limited to the first input line.
		await client.SendLineAsync("WHO", cancellationToken);
		await client.ReadUntilAsync(s => s.Contains("Player Name"), cancellationToken);
		await client.SendLineAsync("PUEBLOCLIENT 2.01", cancellationToken);
		await WaitForFormatAsync(client.Handle, OutputFormat.Pueblo, cancellationToken);

		var output = await PublishKnownMarkupAsync(client, cancellationToken);
		await Assert.That(output.ToUpperInvariant()).Contains("<SEND");
	}

	[Test]
	[Timeout(60_000)]
	public async Task NoPuebloHandshake_OutputRemainsAnsi(CancellationToken cancellationToken)
	{
		await using var client = await ConnectAsync(cancellationToken);
		await WaitForFormatAsync(client.Handle, OutputFormat.Ansi, cancellationToken);

		var output = await PublishKnownMarkupAsync(client, cancellationToken);
		await Assert.That(output).Contains("known-link");
		await Assert.That(output.ToUpperInvariant()).DoesNotContain("<SEND");
		await Assert.That(output.ToUpperInvariant()).DoesNotContain("<FONT");
	}

	[Test]
	[Timeout(60_000)]
	public async Task MxpNegotiation_ServerSendsWillMxp(CancellationToken cancellationToken)
	{
		await using var client = await ConnectAsync(cancellationToken);
		var bytes = await client.ReadRawUntilAsync(data => data.AsSpan().IndexOf(new byte[] { 0xFF, 0xFB, 0x5B }) >= 0,
			cancellationToken);
		await Assert.That(bytes.AsSpan().IndexOf(new byte[] { 0xFF, 0xFB, 0x5B }) >= 0).IsTrue();
	}

	[Test]
	[Timeout(60_000)]
	public async Task MxpNegotiation_ClientAccepts_OutputIncludesMxpTags(CancellationToken cancellationToken)
	{
		await using var client = await ConnectAsync(cancellationToken);
		await client.ReadRawUntilAsync(data => data.AsSpan().IndexOf(new byte[] { 0xFF, 0xFB, 0x5B }) >= 0,
			cancellationToken);
		await client.Stream.WriteAsync(new byte[] { 0xFF, 0xFD, 0x5B }, cancellationToken);
		await WaitForFormatAsync(client.Handle, OutputFormat.Mxp, cancellationToken);

		var output = await PublishKnownMarkupAsync(client, cancellationToken);
		await Assert.That(output).Contains("\x1b[1z");
		await Assert.That(output.ToUpperInvariant()).Contains("<SEND");
	}

	[Test]
	[Timeout(60_000)]
	public async Task MxpNegotiation_ClientRefuses_OutputRemainsAnsi(CancellationToken cancellationToken)
	{
		await using var client = await ConnectAsync(cancellationToken);
		await client.ReadRawUntilAsync(data => data.AsSpan().IndexOf(new byte[] { 0xFF, 0xFB, 0x5B }) >= 0,
			cancellationToken);
		await client.Stream.WriteAsync(new byte[] { 0xFF, 0xFE, 0x5B }, cancellationToken);
		// Processing the following text proves the preceding DONT bytes reached the interpreter.
		await client.SendLineAsync("WHO", cancellationToken);
		await client.ReadUntilAsync(s => s.Contains("Player Name"), cancellationToken);
		await WaitForFormatAsync(client.Handle, OutputFormat.Ansi, cancellationToken);

		var output = await PublishKnownMarkupAsync(client, cancellationToken);
		await Assert.That(output).Contains("known-link");
		await Assert.That(output).DoesNotContain("\x1b[0z");
		await Assert.That(output).DoesNotContain("\x1b[1z");
		await Assert.That(output.ToUpperInvariant()).DoesNotContain("<SEND");
	}

	private async Task<TestConnection> ConnectAsync(CancellationToken ct)
	{
		var connections = Fixture.ConnectionServerServices.GetRequiredService<IConnectionServerService>();
		var previous = connections.GetAll().Select(connection => connection.Handle).ToHashSet();
		var client = new TcpClient();
		try
		{
			await client.ConnectAsync(IPAddress.Loopback, Fixture.TelnetPort, ct);
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
			deadline.CancelAfter(ReceiveTimeoutMs);
			while (true)
			{
				var connection = connections.GetAll().SingleOrDefault(connection => !previous.Contains(connection.Handle));
				if (connection is not null) return new TestConnection(client, connection.Handle);
				await Task.Delay(20, deadline.Token);
			}
		}
		catch { client.Dispose(); throw; }
	}

	private async Task WaitForFormatAsync(long handle, OutputFormat expected, CancellationToken ct)
	{
		var owner = Fixture.ConnectionServerServices.GetRequiredService<IConnectionServerService>();
		var engine = Fixture.ServerServices.GetRequiredService<IConnectionService>();
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
		deadline.CancelAfter(ReceiveTimeoutMs);
		while (true)
		{
			var engineConnection = engine.Get(handle);
			if (owner.Get(handle)?.Capabilities.Format == expected && engineConnection is not null &&
				engineConnection.Metadata.GetValueOrDefault("OUTPUT_FORMAT", "ansi") == expected.ToString().ToLowerInvariant()) return;
			await Task.Delay(20, deadline.Token);
		}
	}

	private async Task<string> PublishKnownMarkupAsync(TestConnection client, CancellationToken ct)
	{
		var marker = Guid.NewGuid().ToString("N");
		var begin = "begin-" + marker;
		var end = "end-" + marker;
		var link = MarkupText.Wrap(HtmlMarkup.Create("send", "href=\"look\""), MarkupText.Plain("known-link"));
		var markup = MarkupText.Concat(MarkupText.Concat(MarkupText.Plain(begin + "\n"), link), MarkupText.Plain("\n" + end));
		await Fixture.ServerServices.GetRequiredService<IMessageBus>().Publish(
			new MarkupOutputMessage(client.Handle, MarkupTextSerializer.Serialize(markup)), ct);
		var received = await client.ReadUntilAsync(s => s.Contains(end), ct);
		var start = received.IndexOf(begin, StringComparison.Ordinal) + begin.Length;
		return received[start..received.IndexOf(end, start, StringComparison.Ordinal)];
	}

	/// <summary>Retains all received bytes so combined welcome/negotiation packets cannot lose data.</summary>
	private sealed class TestConnection(TcpClient client, long handle) : IAsyncDisposable
	{
		private readonly List<byte> _received = [];
		public NetworkStream Stream { get; } = client.GetStream();
		public long Handle { get; } = handle;

		public async Task SendLineAsync(string line, CancellationToken ct) =>
			await Stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\r\n"), ct);

		public async Task<string> ReadUntilAsync(Func<string, bool> condition, CancellationToken ct)
		{
			var raw = await ReadRawUntilAsync(bytes => condition(StripTelnet(bytes)), ct);
			return StripTelnet(raw);
		}

		public async Task<byte[]> ReadRawUntilAsync(Func<byte[], bool> condition, CancellationToken ct)
		{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
			deadline.CancelAfter(ReceiveTimeoutMs);
			var buffer = new byte[4096];
			while (true)
			{
				var bytes = _received.ToArray();
				if (condition(bytes)) return bytes;
				var count = await Stream.ReadAsync(buffer, deadline.Token);
				if (count == 0) throw new EndOfStreamException("Telnet peer closed before the expected output.");
				_received.AddRange(buffer.AsSpan(0, count).ToArray());
			}
		}

		public async ValueTask DisposeAsync()
		{
			await Stream.DisposeAsync();
			client.Dispose();
		}

		private static string StripTelnet(byte[] bytes)
		{
			var text = new List<byte>(bytes.Length);
			for (var i = 0; i < bytes.Length; i++)
			{
				if (bytes[i] != 0xFF) { text.Add(bytes[i]); continue; }
				if (++i >= bytes.Length) break;
				if (bytes[i] is 0xFB or 0xFC or 0xFD or 0xFE) i++;
				else if (bytes[i] == 0xFA)
				{
					while (++i < bytes.Length)
						if (bytes[i] == 0xFF && i + 1 < bytes.Length && bytes[++i] == 0xF0) break;
				}
				else if (bytes[i] == 0xFF) text.Add(0xFF);
			}
			return Encoding.UTF8.GetString(text.ToArray());
		}
	}
}
