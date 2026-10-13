using System.Net.Sockets;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

public class SocketKeepAliveTests
{
	private const int TcpUserTimeoutOption = 18;

	[Test]
	public async Task Apply_enables_keepalive_and_sets_probe_timings()
	{
		using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		var options = new KeepAliveOptions(
			WsInterval: TimeSpan.Zero, WsTimeout: TimeSpan.Zero, TcpUserTimeout: TimeSpan.FromSeconds(47),
			TcpIdle: TimeSpan.FromSeconds(23), TcpProbeInterval: TimeSpan.FromSeconds(7), TcpProbeCount: 4);

		SocketKeepAlive.Apply(socket, options);

		var enabled = (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)!;
		var idle = (int)socket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime)!;
		var interval = (int)socket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval)!;
		var retries = (int)socket.GetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount)!;

		await Assert.That(enabled).IsNotEqualTo(0);
		await Assert.That(idle).IsEqualTo(23);
		await Assert.That(interval).IsEqualTo(7);
		await Assert.That(retries).IsEqualTo(4);

		if (OperatingSystem.IsLinux())
		{
			var userTimeout = new byte[sizeof(int)];
			socket.GetRawSocketOption((int)SocketOptionLevel.Tcp, TcpUserTimeoutOption, userTimeout);
			await Assert.That(BitConverter.ToInt32(userTimeout)).IsEqualTo(47_000);
		}
	}
}
