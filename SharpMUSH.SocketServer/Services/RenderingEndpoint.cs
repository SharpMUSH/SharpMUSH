using System.Net.Sockets;

namespace SharpMUSH.SocketServer.Services;

/// <summary>A rendering worker on a Unix socket beside the socket server, on a volume the two share.</summary>
public readonly record struct RenderSocket(string Path);

/// <summary>
/// A rendering worker at an address on a private network (<c>http://my-game-renderer.internal:5100</c>), for hosts
/// that cannot share a volume between the two. The worker asks for no credentials, so the address is never public.
/// </summary>
public readonly record struct RenderAddress(Uri Uri);

/// <summary>Where the socket server reaches its rendering worker over HTTP/2.</summary>
public union RenderingEndpoint(RenderSocket, RenderAddress);

public static class RenderingEndpoints
{
	public const string DefaultSocketPath = "/run/sharpmush/render.sock";

	/// <summary><c>Rendering:Address</c> when it is set, else <c>Rendering:SocketPath</c>.</summary>
	public static RenderingEndpoint FromConfiguration(IConfiguration configuration) =>
		configuration["Rendering:Address"] is { Length: > 0 } address
			? new RenderAddress(new Uri(address))
			: new RenderSocket(configuration["Rendering:SocketPath"] ?? DefaultSocketPath);

	/// <summary>A handler that connects to the worker; requests go to <see cref="BaseAddress"/>.</summary>
	public static SocketsHttpHandler CreateHandler(RenderingEndpoint endpoint) => endpoint switch
	{
		RenderSocket socket => new SocketsHttpHandler { ConnectCallback = (_, ct) => ConnectAsync(socket.Path, ct) },
		RenderAddress => new SocketsHttpHandler(),
	};

	public static Uri BaseAddress(RenderingEndpoint endpoint) => endpoint switch
	{
		RenderSocket => new Uri("http://renderer"),
		RenderAddress address => address.Uri,
	};

	private static async ValueTask<Stream> ConnectAsync(string path, CancellationToken ct)
	{
		var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
		try
		{
			await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), ct);
			return new NetworkStream(socket, ownsSocket: true);
		}
		catch
		{
			socket.Dispose();
			throw;
		}
	}
}
