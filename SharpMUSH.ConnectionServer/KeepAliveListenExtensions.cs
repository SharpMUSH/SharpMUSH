using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.ConnectionServer;

/// <summary>Adds OS-level TCP keep-alive + TCP_USER_TIMEOUT to every connection on a Kestrel listener.</summary>
public static class KeepAliveListenExtensions
{
	public static void UseTcpKeepAlive(this ListenOptions listenOptions, TimeSpan userTimeout)
	{
		listenOptions.Use(next => async connection =>
		{
			var socket = connection.Features.Get<IConnectionSocketFeature>()?.Socket;
			if (socket is not null)
				SocketKeepAlive.Apply(socket, userTimeout);
			await next(connection);
		});
	}
}
