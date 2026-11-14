using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;

namespace SharpMUSH.SocketServer;

/// <summary>
/// Linux Multipath TCP (RFC 8684) listen sockets for the telnet ports. A client whose OS speaks MPTCP (iOS and
/// macOS through Network.framework, Linux) can then add a second path and keep its session when it moves from
/// Wi-Fi to cellular; every other client is accepted as plain TCP by the same socket.
/// <para>
/// .NET has no MPTCP protocol type, and its socket layer refuses an unknown protocol number before it reaches the
/// kernel, so the socket is opened with libc's <c>socket()</c> and handed to <see cref="Socket"/>. Where the kernel
/// cannot open one (not Linux, MPTCP not built in, or <c>net.mptcp.enabled=0</c> in the container's network
/// namespace) the port is opened as plain TCP instead, with one warning.
/// </para>
/// </summary>
public static class MultipathTcp
{
	/// <summary><c>IPPROTO_MPTCP</c> (linux/in.h).</summary>
	public const int ProtocolNumber = 262;

	// linux/socket.h, linux/net.h, linux/tcp.h.
	private const int AfInet = 2;
	private const int AfInet6 = 10;
	private const int SockStream = 1;
	private const int SockCloexec = 0x80000;
	private const int SolTcp = 6;
	private const int TcpIsMptcp = 43;

	/// <summary>
	/// Opens an unbound MPTCP stream socket, or returns null with the errno that refused it (<c>EPROTONOSUPPORT</c>:
	/// not built into the kernel; <c>ENOPROTOOPT</c>: <c>net.mptcp.enabled</c> is 0; <c>EINVAL</c>: a kernel before
	/// 5.6; <c>EAFNOSUPPORT</c>: no such address family here).
	/// </summary>
	public static Socket? TryOpen(AddressFamily family, out SocketError error)
	{
		error = SocketError.Success;
		if (!OperatingSystem.IsLinux())
		{
			error = SocketError.ProtocolNotSupported;
			return null;
		}

		var domain = family switch
		{
			AddressFamily.InterNetwork => AfInet,
			AddressFamily.InterNetworkV6 => AfInet6,
			_ => -1
		};
		if (domain < 0)
		{
			error = SocketError.AddressFamilyNotSupported;
			return null;
		}

		var descriptor = OpenSocket(domain, SockStream | SockCloexec, ProtocolNumber);
		if (descriptor < 0)
		{
			error = new SocketException(Marshal.GetLastPInvokeError()).SocketErrorCode;
			return null;
		}

		return new Socket(new SafeSocketHandle(descriptor, ownsHandle: true));
	}

	/// <summary>
	/// Whether a connected socket is carrying MPTCP: true when both ends negotiated it, false for a plain TCP socket
	/// or an MPTCP socket that fell back to TCP (<c>TCP_IS_MPTCP</c>, Linux 5.16 and later).
	/// </summary>
	public static bool IsActive(Socket socket)
	{
		if (!OperatingSystem.IsLinux()) return false;
		var value = new byte[sizeof(int)];
		try
		{
			socket.GetRawSocketOption(SolTcp, TcpIsMptcp, value);
			return BitConverter.ToInt32(value) != 0;
		}
		catch (SocketException)
		{
			return false;
		}
	}

	/// <summary>
	/// A <see cref="SocketTransportOptions.CreateBoundListenSocket"/> that opens <paramref name="ports"/> as MPTCP and
	/// every other endpoint as Kestrel does. Port 0 is never in the set, so an ephemeral listener stays plain TCP.
	/// </summary>
	public static Func<EndPoint, Socket> ListenSocketFactory(
		IReadOnlySet<int> ports, Action<int, SocketError> fellBack, Func<AddressFamily, (Socket?, SocketError)>? open = null)
	{
		open ??= family => (TryOpen(family, out var error), error);
		return endpoint => endpoint is IPEndPoint ip && ip.Port != 0 && ports.Contains(ip.Port)
			? BindMultipath(ip, open, fellBack)
			: SocketTransportOptions.CreateDefaultBoundListenSocket(endpoint);
	}

	private static Socket BindMultipath(
		IPEndPoint endpoint, Func<AddressFamily, (Socket?, SocketError)> open, Action<int, SocketError> fellBack)
	{
		var (socket, error) = open(endpoint.AddressFamily);
		if (socket is null)
		{
			// No IPv6 here: the plain socket fails the same way, and Kestrel's "any IP" listener retries on IPv4,
			// which comes back through this factory. Only a refusal of MPTCP itself is worth reporting.
			if (error != SocketError.AddressFamilyNotSupported) fellBack(endpoint.Port, error);
			return SocketTransportOptions.CreateDefaultBoundListenSocket(endpoint);
		}

		try
		{
			// As CreateDefaultBoundListenSocket: Kestrel's "any IP" listener binds [::] and expects IPv4 on it too.
			if (endpoint.Address.Equals(IPAddress.IPv6Any)) socket.DualMode = true;
			socket.Bind(endpoint);
			return socket;
		}
		catch
		{
			socket.Dispose();
			throw;
		}
	}

	// Every argument is blittable, so this needs no marshalling stub: Native AOT binds it directly. (LibraryImport
	// would need AllowUnsafeBlocks for nothing.)
	[DllImport("libc", EntryPoint = "socket", SetLastError = true)]
	private static extern int OpenSocket(int domain, int type, int protocol);
}
