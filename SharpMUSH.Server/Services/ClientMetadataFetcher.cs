using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.OAuth;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Reads the description an app serves at its https <c>client_id</c> (<see cref="ClientIdMetadata"/>). Anyone can send
/// a player to the consent page with any address, so the reader is careful where it goes: only to an address on the
/// public internet (checked on the address it actually connects to, so a name that resolves to the game's own network
/// is refused), never following a redirect, sending no cookies or credentials, and reading at most
/// <see cref="ClientIdMetadata.MaxDocumentBytes"/> within ten seconds, and only a few at once.
/// </summary>
public sealed class ClientMetadataFetcher(IHttpClientFactory factory, ILogger<ClientMetadataFetcher> logger) : IClientMetadataFetcher
{
	/// <summary>The named client the server registers with <see cref="Handler"/>.</summary>
	public const string HttpClientName = "oauth-client-metadata";

	/// <summary>
	/// The configuration key listing hosts whose description may be read from a private address, for trying an app on
	/// a development machine. Empty in a real deployment.
	/// </summary>
	public const string PrivateHostsKey = "OAuth:AllowPrivateMetadataHosts";

	/// <summary>The longest a whole read may take, headers and body together.</summary>
	public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

	/// <summary>The most reads in flight at once, across every app.</summary>
	public const int MaxConcurrentReads = 8;

	private readonly SemaphoreSlim _slots = new(MaxConcurrentReads, MaxConcurrentReads);

	public async ValueTask<Result<string>> FetchAsync(string clientId, CancellationToken ct = default)
	{
		if (!ClientIdMetadata.IsClientIdUrl(clientId)) return new Error<string>("The client_id is not an https address with a path.");
		// One deadline for waiting, connecting, the headers and the body: a server that sends a byte now and then is cut off.
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
		deadline.CancelAfter(Deadline);
		try
		{
			await _slots.WaitAsync(deadline.Token);
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested)
		{
			return new Error<string>("The game is reading too many app descriptions; try again in a moment.");
		}

		try
		{
			return await ReadAsync(clientId, deadline.Token);
		}
		catch (Exception ex) when (ex is HttpRequestException or IOException)
		{
			logger.LogInformation("Could not read the app description at {ClientId}: {Reason}", clientId, ex.Message);
			return new Error<string>("The address could not be reached.");
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested)
		{
			return new Error<string>("The address took too long to answer.");
		}
		finally
		{
			_slots.Release();
		}
	}

	private async Task<Result<string>> ReadAsync(string clientId, CancellationToken ct)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, clientId);
		request.Headers.Accept.ParseAdd("application/json");
		using var client = factory.CreateClient(HttpClientName);
		using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
		if (response.StatusCode != HttpStatusCode.OK)
			return new Error<string>($"The address answered {(int)response.StatusCode}.");
		if (response.Content.Headers.ContentLength > ClientIdMetadata.MaxDocumentBytes)
			return new Error<string>($"The description is larger than {ClientIdMetadata.MaxDocumentBytes} bytes.");

		await using var stream = await response.Content.ReadAsStreamAsync(ct);
		var buffer = new byte[ClientIdMetadata.MaxDocumentBytes + 1];
		var length = 0;
		int read;
		while (length < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(length), ct)) > 0) length += read;
		return length > ClientIdMetadata.MaxDocumentBytes
			? new Error<string>($"The description is larger than {ClientIdMetadata.MaxDocumentBytes} bytes.")
			: Encoding.UTF8.GetString(buffer, 0, length);
	}

	/// <summary>
	/// The handler behind <see cref="HttpClientName"/>: no redirects or cookies, and a connection only to a public
	/// address, except for a host named in <paramref name="privateHosts"/>.
	/// </summary>
	public static SocketsHttpHandler Handler(IReadOnlyCollection<string> privateHosts) => new()
	{
		AllowAutoRedirect = false,
		UseCookies = false,
		UseProxy = false,
		ConnectTimeout = TimeSpan.FromSeconds(5),
		ConnectCallback = async (context, ct) =>
		{
			var host = context.DnsEndPoint.Host;
			var anywhere = privateHosts.Contains(host, StringComparer.OrdinalIgnoreCase);
			var addresses = await Dns.GetHostAddressesAsync(host, ct);
			var address = addresses.FirstOrDefault(candidate => anywhere || IsPublic(candidate))
				?? throw new HttpRequestException($"{host} is not on the public internet.");
			var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
			try
			{
				await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
				return new NetworkStream(socket, ownsSocket: true);
			}
			catch
			{
				socket.Dispose();
				throw;
			}
		},
	};

	/// <summary>
	/// Whether <paramref name="address"/> is on the public internet: not this machine, a private or carrier network,
	/// link-local, multicast, reserved or documentation space, nor an IPv6 form that wraps an IPv4 address.
	/// </summary>
	public static bool IsPublic(IPAddress address)
	{
		if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
		if (IPAddress.IsLoopback(address)) return false;
		if (address.AddressFamily == AddressFamily.InterNetwork)
		{
			var b = address.GetAddressBytes();
			return b[0] switch
			{
				0 or 10 or 127 => false,
				>= 224 => false,
				100 when b[1] is >= 64 and <= 127 => false,
				169 when b[1] == 254 => false,
				172 when b[1] is >= 16 and <= 31 => false,
				192 when b[1] == 168 => false,
				192 when b[1] == 0 && b[2] is 0 or 2 => false,
				198 when b[1] is 18 or 19 => false,
				198 when b[1] == 51 && b[2] == 100 => false,
				203 when b[1] == 0 && b[2] == 113 => false,
				_ => true,
			};
		}

		if (address.AddressFamily != AddressFamily.InterNetworkV6) return false;
		if (address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
				|| address.IsIPv6UniqueLocal || address.IsIPv6Teredo || address.IsIPv4MappedToIPv6)
			return false;
		var bytes = address.GetAddressBytes();
		// 2001:db8::/32 documentation, 2002::/16 6to4 and 64:ff9b::/96 NAT64 can stand for any IPv4 address, a private one too.
		if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8) return false;
		if (bytes[0] == 0x20 && bytes[1] == 0x02) return false;
		if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xff && bytes[3] == 0x9b) return false;
		// ::/96 (IPv4-compatible) and the other addresses under ::/8 are not on the public internet.
		return bytes[0] != 0x00;
	}
}
