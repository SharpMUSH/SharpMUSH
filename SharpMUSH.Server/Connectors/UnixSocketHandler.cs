using System.Net.Sockets;

namespace SharpMUSH.Server.Connectors;

public static class UnixSocketHandler
{
	public static SocketsHttpHandler CreateHandlerForUnixSocket(string socketPath)
	{
		return new SocketsHttpHandler
		{
			ConnectCallback = async (context, cancellationToken) =>
			{
				var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
				try
				{
					var endpoint = new UnixDomainSocketEndPoint(socketPath);
					await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
					return new NetworkStream(socket, ownsSocket: true);
				}
				catch
				{
					socket.Dispose();
					throw;
				}
			}
		};
	}
}