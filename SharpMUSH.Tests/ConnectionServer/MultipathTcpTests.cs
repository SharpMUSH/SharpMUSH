using System.Net;
using System.Net.Sockets;
using SharpMUSH.SocketServer;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// The telnet listen-socket factory on real sockets: MPTCP where the kernel has it, plain TCP for every other
/// client and wherever it does not.
/// </summary>
public class MultipathTcpTests
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

	[Test]
	public async Task ListedPortAcceptsMultipathAndPlainTcpClients()
	{
		using var probe = MultipathTcp.TryOpen(AddressFamily.InterNetwork, out var unavailable);
		if (probe is null) Skip.Test($"This kernel opens no MPTCP socket ({unavailable})");

		using var timeout = new CancellationTokenSource(Timeout);
		var port = GetUnusedPort();
		var fellBack = new List<SocketError>();
		var factory = MultipathTcp.ListenSocketFactory(new HashSet<int> { port }, (_, error) => fellBack.Add(error));
		using var listener = factory(new IPEndPoint(IPAddress.Loopback, port));
		listener.Listen();

		using (var client = MultipathTcp.TryOpen(AddressFamily.InterNetwork, out _)!)
		{
			await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
			using var accepted = await listener.AcceptAsync(timeout.Token);
			await Assert.That(MultipathTcp.IsActive(accepted)).IsTrue();
			await Assert.That(MultipathTcp.IsActive(client)).IsTrue();
			await AssertCarriesBytesAsync(client, accepted, timeout.Token);
		}

		using (var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
		{
			await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
			using var accepted = await listener.AcceptAsync(timeout.Token);
			await Assert.That(MultipathTcp.IsActive(accepted)).IsFalse();
			await AssertCarriesBytesAsync(client, accepted, timeout.Token);
		}

		await Assert.That(fellBack).IsEmpty();
	}

	[Test]
	public async Task UnlistedPortIsPlainTcp()
	{
		var port = GetUnusedPort();
		var factory = MultipathTcp.ListenSocketFactory(new HashSet<int> { port + 1 }, (_, _) => { },
			_ => throw new InvalidOperationException("An unlisted port must not ask for MPTCP"));
		using var listener = factory(new IPEndPoint(IPAddress.Loopback, port));

		await Assert.That(listener.ProtocolType).IsEqualTo(ProtocolType.Tcp);
		await Assert.That(((IPEndPoint)listener.LocalEndPoint!).Port).IsEqualTo(port);
	}

	[Test]
	public async Task RefusedMultipathFallsBackToPlainTcpAndReportsWhy()
	{
		using var timeout = new CancellationTokenSource(Timeout);
		var port = GetUnusedPort();
		var fellBack = new List<(int Port, SocketError Error)>();
		var factory = MultipathTcp.ListenSocketFactory(new HashSet<int> { port }, (p, error) => fellBack.Add((p, error)),
			_ => (null, SocketError.ProtocolOption));
		using var listener = factory(new IPEndPoint(IPAddress.Loopback, port));
		listener.Listen();

		using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
		using var accepted = await listener.AcceptAsync(timeout.Token);

		await Assert.That(listener.ProtocolType).IsEqualTo(ProtocolType.Tcp);
		await AssertCarriesBytesAsync(client, accepted, timeout.Token);
		await Assert.That(fellBack).IsEquivalentTo([(port, SocketError.ProtocolOption)]);
	}

	[Test]
	public async Task MissingAddressFamilyIsNotReportedAsMissingMultipath()
	{
		var port = GetUnusedPort();
		var fellBack = new List<SocketError>();
		var factory = MultipathTcp.ListenSocketFactory(new HashSet<int> { port }, (_, error) => fellBack.Add(error),
			_ => (null, SocketError.AddressFamilyNotSupported));
		using var listener = factory(new IPEndPoint(IPAddress.Loopback, port));

		await Assert.That(listener.ProtocolType).IsEqualTo(ProtocolType.Tcp);
		await Assert.That(fellBack).IsEmpty();
	}

	[Test]
	public async Task PlainTcpSocketIsNotActive()
	{
		using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		await Assert.That(MultipathTcp.IsActive(socket)).IsFalse();
	}

	private static async Task AssertCarriesBytesAsync(Socket from, Socket to, CancellationToken ct)
	{
		var payload = "look\r\n"u8.ToArray();
		await from.SendAsync(payload, SocketFlags.None, ct);
		var received = new byte[payload.Length];
		var total = 0;
		while (total < received.Length)
		{
			var read = await to.ReceiveAsync(received.AsMemory(total), SocketFlags.None, ct);
			if (read == 0) break;
			total += read;
		}
		await Assert.That(received).IsEquivalentTo(payload);
	}

	private static int GetUnusedPort()
	{
		using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
		return ((IPEndPoint)socket.LocalEndPoint!).Port;
	}
}
