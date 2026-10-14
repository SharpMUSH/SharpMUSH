using System.Net;
using System.Net.Sockets;

namespace SharpMUSH.Library.Webhooks;

/// <summary>
/// One event as it is delivered: its id (the <c>webhook-id</c> header, the same on every try, so a
/// receiver can drop a repeat), the event's name and the JSON body.
/// </summary>
public sealed record WebhookMessage(string Id, string EventName, string Body, DateTimeOffset CreatedAt);

/// <summary>Which addresses a webhook may be sent to.</summary>
public static class WebhookAddress
{
	/// <summary>
	/// <paramref name="url"/> cut to its scheme and host, for the audit log: some services put the
	/// credential in the path (a Discord webhook's token), which nothing should copy elsewhere.
	/// </summary>
	public static string Redacted(string? url)
		=> string.IsNullOrWhiteSpace(url) ? "none"
			: Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) + "/…" : "…";

	/// <summary>
	/// Whether <paramref name="address"/> is on the public internet: not loopback, private, link-local,
	/// carrier-grade NAT, unique-local or unspecified.
	/// </summary>
	public static bool IsPublic(IPAddress address)
	{
		if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
		if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
			|| address.Equals(IPAddress.Broadcast))
		{
			return false;
		}

		if (address.AddressFamily == AddressFamily.InterNetworkV6)
		{
			// fc00::/7 unique-local, fe80::/10 link-local, plus the obsolete site-local fec0::/10.
			var first = address.GetAddressBytes()[0];
			return (first & 0xFE) != 0xFC && !address.IsIPv6LinkLocal && !address.IsIPv6SiteLocal && !address.IsIPv6Multicast;
		}

		var b = address.GetAddressBytes();
		return b[0] switch
		{
			0 or 10 or 127 => false,
			100 => b[1] is < 64 or > 127,
			169 => b[1] != 254,
			172 => b[1] is < 16 or > 31,
			192 => !(b[1] == 168 || (b[1] == 0 && b[2] is 0 or 2)),
			198 => b[1] is not (18 or 19),
			>= 224 => false,
			_ => true,
		};
	}
}
