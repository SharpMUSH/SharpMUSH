using System.Net;
using System.Net.Sockets;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Discord.Links;

/// <summary>The picture a feed line goes out to Discord with, as the webhook post's avatar.</summary>
public interface IDiscordAvatars
{
	/// <summary>An image address Discord can fetch, or <see cref="NotFound"/> for the webhook's own avatar.</summary>
	ValueTask<Found<string>> AvatarAsync(SharpFeedMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// A line goes out with its speaker's portrait: the <c>IMAGE</c> attribute the portal's gallery keeps, when it is
/// <c>visual</c>, as the portal's profiles show it. Without one it goes out with the picture it came with from outside
/// (<see cref="SharpFeedMessage.Picture"/>, a Discord member's avatar), and a Discord member speaking without a
/// character keeps their Discord avatar. An emit, an announcement and a line sent under another name
/// (<c>@feed/send/as</c>) carry no speaker's portrait.
/// </summary>
/// <remarks>The host's services are resolved when first used: some are built after the plugin's.</remarks>
public sealed class DiscordAvatars(IServiceProvider services) : IDiscordAvatars
{
	public const string ImageAttribute = "IMAGE";

	public async ValueTask<Found<string>> AvatarAsync(SharpFeedMessage message, CancellationToken cancellationToken)
	{
		var portrait = Speaker(message) is string speaker ? await PortraitAsync(speaker, cancellationToken) : new NotFound();
		return portrait switch
		{
			string picture => Fetchable(picture),
			NotFound => Outside(message) is string outside ? Fetchable(outside) : new NotFound(),
		};
	}

	private Found<string> Fetchable(string picture)
		=> DiscordAvatarUrl.Public(picture, services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Net.MudUrl);

	/// <summary>The picture the line came with from outside: an outside speaker's, or the one it was sent with.</summary>
	private static Found<string> Outside(SharpFeedMessage message)
		=> NonEmpty(message.ExternalIdentity is { } external ? external.Picture : message.Picture);

	/// <summary>
	/// The character whose portrait the line carries, by objid: its speaker, unless the line is an emit, an
	/// announcement, sent under another name, or from someone outside the game; <see cref="NotFound"/> then.
	/// </summary>
	public static Found<string> Speaker(SharpFeedMessage message)
		=> message.ExternalIdentity is not null || message.Style is FeedStyles.Emit or FeedStyles.Announce
			|| message.DisplayName.Length > 0 || message.Speaker.Number < 0
				? new NotFound()
				: message.Speaker.ToString();

	private async ValueTask<Found<string>> PortraitAsync(string objid, CancellationToken cancellationToken)
	{
		var mediator = services.GetRequiredService<IMediator>();
		if (!DBRef.TryParse(objid, out var parsed) || parsed is not { } dbref
			|| await mediator.Send(new GetObjectNodeQuery(dbref), cancellationToken) is not AnySharpObject found)
			return new NotFound();

		// As the portal's profile reads it: only a visual IMAGE is anyone's to see.
		return await mediator.CreateStream(new GetAttributeQuery(found.Object().DBRef, [ImageAttribute]), cancellationToken)
			.LastOrDefaultAsync(cancellationToken) is { } attribute && attribute.IsVisual()
			&& attribute.Value.ToPlainText().Trim() is { Length: > 0 } image
			? image
			: new NotFound();
	}

	private static Found<string> NonEmpty(string value) => string.IsNullOrWhiteSpace(value) ? new NotFound() : value.Trim();
}

/// <summary>Turns a picture into an address Discord's servers can fetch.</summary>
public static class DiscordAvatarUrl
{
	/// <summary>The longest avatar address the bridge hands Discord.</summary>
	public const int MaxLength = 2048;

	/// <summary>
	/// The picture as an absolute http(s) address on a public host, or <see cref="NotFound"/>. A path from the root is one of the game's
	/// own pictures, served from <paramref name="mudUrl"/> (as PNG when stored in another format); with no web address
	/// set there is nowhere for Discord to fetch it from. An address on this machine or a private network is left out,
	/// since Discord could not reach it either.
	/// </summary>
	public static Found<string> Public(string picture, string? mudUrl)
	{
		var trimmed = picture.Trim();
		Uri? address;
		if (trimmed.StartsWith('/') && !trimmed.StartsWith("//", StringComparison.Ordinal))
		{
			if (!Uri.TryCreate(mudUrl?.Trim(), UriKind.Absolute, out var site) || !IsWeb(site)) return new NotFound();
			address = new Uri(new Uri(site.GetLeftPart(UriPartial.Authority)), PngPictures.Readable(trimmed));
		}
		else if (!Uri.TryCreate(trimmed, UriKind.Absolute, out address))
		{
			return new NotFound();
		}

		var text = address.AbsoluteUri;
		return IsWeb(address) && IsPublicHost(address) && text.Length <= MaxLength ? text : new NotFound();
	}

	private static bool IsWeb(Uri address) =>
		(address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp) && address.UserInfo.Length == 0;

	/// <summary>A host Discord could reach: a name outside the local-only zones, or a public IP address.</summary>
	public static bool IsPublicHost(Uri address)
	{
		if (address.HostNameType is UriHostNameType.Dns)
		{
			var host = address.IdnHost.TrimEnd('.').ToLowerInvariant();
			return host.Contains('.') && !LocalZones.Any(zone => host == zone || host.EndsWith("." + zone, StringComparison.Ordinal));
		}

		return IPAddress.TryParse(address.DnsSafeHost, out var ip) && IsPublicAddress(ip);
	}

	private static readonly string[] LocalZones = ["localhost", "local", "internal", "lan", "home.arpa", "test", "invalid", "example"];

	private static bool IsPublicAddress(IPAddress ip)
	{
		if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
		if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return false;
		if (ip.AddressFamily == AddressFamily.InterNetworkV6)
		{
			var first = ip.GetAddressBytes()[0];
			return !ip.IsIPv6LinkLocal && !ip.IsIPv6SiteLocal && !ip.IsIPv6Multicast && (first & 0xFE) != 0xFC;
		}

		var b = ip.GetAddressBytes();
		return !(b[0] == 10
			|| b[0] == 0
			|| (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
			|| (b[0] == 169 && b[1] == 254)
			|| (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
			|| (b[0] == 192 && b[1] == 168)
			|| b[0] >= 224);
	}
}
