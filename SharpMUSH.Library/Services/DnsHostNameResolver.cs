using System.Net;
using System.Net.Sockets;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// A reverse lookup through the system resolver, given up after <see cref="Timeout"/>. A name is kept only when it
/// resolves back to the address and holds no control characters: whoever runs an address's reverse zone can give it
/// any name, so an unconfirmed one could pass for a site a host rule names. PennMUSH's <c>info_slave</c> keeps the
/// reverse answer unconfirmed.
/// </summary>
public sealed class DnsHostNameResolver : IHostNameResolver
{
	/// <summary>How long a lookup may take before the address stands as the host.</summary>
	public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

	/// <inheritdoc />
	public async ValueTask<string?> ResolveAsync(string address, CancellationToken cancellationToken = default)
	{
		if (!IPAddress.TryParse(address, out var parsed)) return null;
		var ip = parsed.IsIPv4MappedToIPv6 ? parsed.MapToIPv4() : parsed;

		using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		limit.CancelAfter(Timeout);
		try
		{
			// For an address, GetHostEntryAsync looks the name up and then the name's addresses.
			var entry = await Dns.GetHostEntryAsync(ip.ToString(), limit.Token);
			return string.IsNullOrWhiteSpace(entry.HostName)
				|| entry.HostName == address
				|| entry.HostName.Any(char.IsControl)
				|| !entry.AddressList.Any(listed => (listed.IsIPv4MappedToIPv6 ? listed.MapToIPv4() : listed).Equals(ip))
					? null
					: entry.HostName;
		}
		catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException)
		{
			return null;
		}
	}
}
