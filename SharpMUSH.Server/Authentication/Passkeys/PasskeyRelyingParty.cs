using Fido2NetLib;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Authentication.Passkeys;

/// <summary>
/// Who the portal is, as WebAuthn sees it: the origin a ceremony runs on and the relying-party id
/// (the host) its passkeys are scoped to.
/// </summary>
/// <remarks>
/// <para>The portal is normally served by this server, so a page on the request's own host is the
/// origin, and nothing needs configuring. A portal served from
/// somewhere else (the standalone client dev server, a CDN) is accepted when its origin is listed in
/// <c>Passkeys:Origins</c> or, failing that, <c>Cors:AllowedOrigins</c> — the browser's
/// <c>Origin</c> header says which one is asking. <c>Passkeys:RelyingPartyId</c> widens the scope to
/// a parent domain (<c>example.com</c> for <c>play.example.com</c> and <c>www.example.com</c>).</para>
///
/// <para>A passkey only works on the relying-party id it was made for. Moving the portal to another
/// host leaves its passkeys behind; their holders sign in with a password and add new ones.</para>
/// </remarks>
public sealed class PasskeyRelyingParty(IConfiguration configuration, IOptionsWrapper<SharpMUSHOptions> options)
{
	/// <summary>The relying party for a ceremony asked for by <paramref name="request"/>.</summary>
	public Result<Fido2Configuration> For(HttpRequest request)
	{
		var origin = request.Headers.Origin.FirstOrDefault() is { Length: > 0 } header
			? header.TrimEnd('/')
			: $"{request.Scheme}://{request.Host}";

		// The host alone decides "this server's own page": behind a proxy whose forwarded scheme is not
		// trusted the request reads as plain http, while the browser's page is https.
		if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
			|| (!string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase) && !Listed(origin)))
			return new Error<string>("Passkeys are not available from this address.");

		var rpId = configuration["Passkeys:RelyingPartyId"] is { Length: > 0 } configured ? configured : uri.Host;
		var name = options.CurrentValue.Net.MudName is { Length: > 0 } mudName ? mudName : "SharpMUSH";

		return new Fido2Configuration
		{
			RPID = rpId,
			RPName = name,
			Origins = new HashSet<string>([origin], StringComparer.OrdinalIgnoreCase),
		};
	}

	private bool Listed(string origin)
	{
		var listed = configuration.GetSection("Passkeys:Origins").Get<string[]>() is { Length: > 0 } passkeyOrigins
			? passkeyOrigins
			: configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
		return listed.Any(o => string.Equals(o.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase));
	}
}
