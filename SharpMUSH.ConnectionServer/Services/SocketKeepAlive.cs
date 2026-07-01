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
		// SO_KEEPALIVE is universally supported; the per-probe timings are the cross-platform tuning
		// (Windows + Linux). Each is best-effort so an option a platform lacks (e.g.
		// TcpKeepAliveRetryCount on macOS) is skipped rather than failing the whole connection.
		TrySet(socket, SocketOptionLevel.Socket, SocketOptionName.KeepAlive, 1);
		TrySet(socket, SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, idleSeconds);
		TrySet(socket, SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, intervalSeconds);
		TrySet(socket, SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, retryCount);

		// TCP_USER_TIMEOUT caps how long unacknowledged (in-flight) data may remain outstanding before
		// the connection is dropped — this catches a peer that vanishes mid-send, which the idle-only
		// keep-alive probes above do not. Linux-only; there is no portable .NET equivalent. Best-effort.
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

	private static void TrySet(Socket socket, SocketOptionLevel level, SocketOptionName name, int value)
	{
		try
		{
			socket.SetSocketOption(level, name, value);
		}
		catch (Exception ex) when (ex is SocketException or PlatformNotSupportedException)
		{
			// Option not supported on this platform — skip it and keep the ones that are.
		}
	}
}
