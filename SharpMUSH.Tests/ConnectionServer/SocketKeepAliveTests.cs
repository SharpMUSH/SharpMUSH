using System.Net.Sockets;
using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

public class SocketKeepAliveTests
{
	[Test]
	public async Task Apply_enables_keepalive_and_sets_probe_timings()
	{
		using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

		SocketKeepAlive.Apply(socket, TimeSpan.FromSeconds(20), idleSeconds: 20, intervalSeconds: 5, retryCount: 3);

		var enabled = (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)!;
		var idle = (int)socket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime)!;
		var interval = (int)socket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval)!;
		var retries = (int)socket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount)!;

		await Assert.That(enabled).IsNotEqualTo(0);
		await Assert.That(idle).IsEqualTo(20);
		await Assert.That(interval).IsEqualTo(5);
		await Assert.That(retries).IsEqualTo(3);
	}
}
