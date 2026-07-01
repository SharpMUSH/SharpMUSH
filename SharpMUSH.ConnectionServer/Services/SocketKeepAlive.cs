using System.Net.Sockets;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Applies OS-level TCP keep-alive + <c>TCP_USER_TIMEOUT</c> to a socket so a half-open connection
/// (abrupt drop with no FIN/RST — e.g. a WiFi→cellular switch) is detected in seconds rather than the
/// multi-minute TCP default. Used on both the telnet and WebSocket/HTTP listeners so a dropped session
/// is noticed promptly, which is when the detached-session grace window can start.
/// </summary>
public static class SocketKeepAlive
{
	// Linux TCP_USER_TIMEOUT (netinet/tcp.h); no named .NET enum exists for it.
	private const int TcpUserTimeoutOption = 18;

	public static void Apply(Socket socket, TimeSpan userTimeout, int idleSeconds = 20, int intervalSeconds = 5, int retryCount = 3)
	{
		socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
		socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, idleSeconds);
		socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, intervalSeconds);
		socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, retryCount);

		// TCP_USER_TIMEOUT caps how long unacknowledged data may remain outstanding before the
		// connection is dropped — the decisive knob for half-open detection. Linux-only; best-effort.
		if (OperatingSystem.IsLinux())
		{
			try
			{
				socket.SetRawSocketOption(
					(int)SocketOptionLevel.Tcp, TcpUserTimeoutOption,
					BitConverter.GetBytes((int)userTimeout.TotalMilliseconds));
			}
			catch (SocketException)
			{
				// Kernel without TCP_USER_TIMEOUT support — the keep-alive probes above still apply.
			}
		}
	}
}
