using System.Net;
using System.Net.Sockets;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>A reverse lookup through the system resolver, given up after <see cref="Timeout"/>.</summary>
public sealed class DnsHostNameResolver : IHostNameResolver
{
	/// <summary>How long a lookup may take before the address stands as the host.</summary>
	public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

	/// <inheritdoc />
	public async ValueTask<string?> ResolveAsync(string address, CancellationToken cancellationToken = default)
	{
		if (!IPAddress.TryParse(address, out var ip)) return null;

		using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		limit.CancelAfter(Timeout);
		try
		{
			var entry = await Dns.GetHostEntryAsync(ip.ToString(), limit.Token);
			return string.IsNullOrWhiteSpace(entry.HostName) || entry.HostName == address ? null : entry.HostName;
		}
		catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException)
		{
			return null;
		}
	}
}
