using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.SocketServer.Services;
using TelnetNegotiationCore.WebSockets;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// <c>/telnet</c>: telnet carried in a WebSocket, as MUD clients that connect over <c>wss://</c> send it, on the real
/// connection-server host. It is the same telnet connection the telnet port runs.
/// </summary>
public class TelnetWebSocketEndpointTests
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

	[Test]
	[Arguments(MudWebSocketSubprotocols.Telnet)]
	[Arguments(MudWebSocketSubprotocols.TelnetAlias)]
	[Arguments(null)]
	public async Task ATelnetWebSocketIsATelnetConnection(string? subprotocol)
	{
		using var timeout = new CancellationTokenSource(Timeout);
		await using var server = await StartAsync(timeout.Token);
		using var socket = new ClientWebSocket();
		if (subprotocol is not null) socket.Options.AddSubProtocol(subprotocol);
		await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{server.HttpPort}/telnet"), timeout.Token);
		await Assert.That(socket.SubProtocol).IsEqualTo(subprotocol);

		// The same exchange the telnet port is held to: registered as telnet, negotiation, input and output.
		await using var stream = new WebSocketStream(socket);
		await NativeTelnetTlsTests.ExchangeTelnetAsync(server, stream, isSecure: false, timeout.Token);
	}

	[Test]
	public async Task TheClientLeavingEndsTheConnection()
	{
		using var timeout = new CancellationTokenSource(Timeout);
		await using var server = await StartAsync(timeout.Token);
		using var socket = new ClientWebSocket();
		socket.Options.AddSubProtocol(MudWebSocketSubprotocols.Telnet);
		await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{server.HttpPort}/telnet"), timeout.Token);
		var established = await server.Backend.Established.Reader.ReadAsync(timeout.Token);

		await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token);

		var connections = server.App.Services.GetRequiredService<IConnectionServerService>();
		while (connections.Get(established.Handle) is not null)
			await Task.Delay(20, timeout.Token);
		await Assert.That(socket.CloseStatus).IsEqualTo(WebSocketCloseStatus.NormalClosure);
	}

	[Test]
	public async Task ASocketThatOnlySpeaksAnotherSubprotocolIsRefused()
	{
		using var timeout = new CancellationTokenSource(Timeout);
		await using var server = await StartAsync(timeout.Token);
		using var socket = new ClientWebSocket();
		socket.Options.AddSubProtocol(MudWebSocketSubprotocols.Gmcp);

		await Assert.That(async () =>
				await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{server.HttpPort}/telnet"), timeout.Token))
			.Throws<WebSocketException>();
	}

	/// <summary>The connection server's host with WebSockets and the endpoint as Program maps it; no broker or portal.</summary>
	private static Task<NativeTelnetTlsTests.TestServer> StartAsync(CancellationToken ct) =>
		NativeTelnetTlsTests.TestServer.StartAsync(0, null, ct, app =>
		{
			app.UseWebSockets();
			SharpMUSH.SocketServer.Program.MapTelnetWebSocket(app);
		});

	/// <summary>A WebSocket's BINARY messages as a byte stream, enough for the telnet exchange.</summary>
	private sealed class WebSocketStream(WebSocket socket) : Stream
	{
		public override bool CanRead => true;
		public override bool CanWrite => true;
		public override bool CanSeek => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			var result = await socket.ReceiveAsync(buffer, cancellationToken);
			return result.MessageType == WebSocketMessageType.Close ? 0 : result.Count;
		}

		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
			socket.SendAsync(buffer, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
	}
}
