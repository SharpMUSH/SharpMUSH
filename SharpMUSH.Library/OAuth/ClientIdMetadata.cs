using System.Text.Json;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.OAuth;

/// <summary>
/// OAuth Client ID Metadata Documents (IETF OAuth working group draft): an app whose <c>client_id</c> is an https
/// address serving a JSON description of itself, which the game reads the first time a player is sent its way, so
/// staff need not register it. Only public apps are accepted this way: they prove themselves with PKCE alone, as a
/// desktop or browser app registered by staff does.
/// </summary>
public static class ClientIdMetadata
{
	/// <summary>The most of a document the game reads, as the draft recommends.</summary>
	public const int MaxDocumentBytes = 5 * 1024;

	/// <summary>The most self-described apps kept waiting for staff; past it a new one is refused until staff decide.</summary>
	public const int MaxWaiting = 100;

	/// <summary>How long a document read is trusted before it is read again.</summary>
	public static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(1);

	private static readonly JsonDocumentOptions Parsing = new() { MaxDepth = 8 };

	/// <summary>
	/// Whether <paramref name="clientId"/> names an app by its metadata address: https to a host name (not an IP address,
	/// no trailing dot), with a path, no user information, no fragment, no <c>.</c> or <c>..</c> path segment, at most
	/// <see cref="OAuthService.MaxUriLength"/> characters.
	/// </summary>
	public static bool IsClientIdUrl(string? clientId)
	{
		if (clientId is null || clientId.Length > OAuthService.MaxUriLength
				|| !clientId.StartsWith("https://", StringComparison.Ordinal) || clientId.Contains('#')
				|| !Uri.TryCreate(clientId, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Host.Length == 0
				// A name, never an address, and without the trailing dot that would let one site go by two names.
				|| uri.HostNameType != UriHostNameType.Dns || uri.IdnHost.EndsWith('.'))
			return false;
		// Read the path as written: Uri removes dot segments, and those are what must be refused.
		var rest = clientId["https://".Length..];
		var start = rest.IndexOfAny(['/', '?']);
		var path = start < 0 ? string.Empty : rest[start..].Split('?')[0];
		if (path.Length < 2) return false;
		var segments = path.Split('/');
		return !segments.Any(segment => segment is "." or ".." || segment.Equals("%2e", StringComparison.OrdinalIgnoreCase)
			|| segment.Equals("%2e%2e", StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>The host of a metadata <c>client_id</c>, lower case: what the consent page says the details came from.</summary>
	public static string HostOf(string clientId) => new Uri(clientId).IdnHost.ToLowerInvariant();

	/// <summary>
	/// Whether <paramref name="clientId"/>'s host is one of <paramref name="blockedDomains"/> or under one of them.
	/// A blocked entry may be written as a domain (<c>example.com</c>), with a leading dot, or as a whole address.
	/// </summary>
	public static bool IsBlocked(string clientId, IEnumerable<string>? blockedDomains)
	{
		if (blockedDomains is null || !IsClientIdUrl(clientId)) return false;
		var host = HostOf(clientId);
		return blockedDomains.Select(DomainOf)
			.Any(domain => domain.Length > 0 && (host == domain || host.EndsWith("." + domain, StringComparison.Ordinal)));
	}

	/// <summary>An entry of the block list as a bare lower-case domain: an address keeps its host, a leading dot goes.</summary>
	public static string DomainOf(string entry)
	{
		var trimmed = entry.Trim().TrimStart('.').ToLowerInvariant();
		if (trimmed.Contains("://", StringComparison.Ordinal))
			return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ? uri.IdnHost.ToLowerInvariant() : string.Empty;
		return trimmed.TrimEnd('/');
	}

	/// <summary>
	/// Whether an app described at <paramref name="clientId"/> may send codes to <paramref name="redirectUri"/>: an https
	/// address on the same host, so a site cannot describe itself as an app that sends codes elsewhere; http to the
	/// machine itself, for a desktop app (RFC 8252 §7.3); or a private scheme named for the host reversed, such as
	/// <c>com.example.valyria:/callback</c> for <c>valyria.example.com</c> (RFC 8252 §7.1).
	/// </summary>
	public static bool RedirectFits(string clientId, string redirectUri)
	{
		if (!OAuthService.IsRedirectUri(redirectUri) || !Uri.TryCreate(redirectUri, UriKind.Absolute, out var redirect)) return false;
		var host = HostOf(clientId);
		return redirect.Scheme switch
		{
			"https" => redirect.IdnHost.Equals(host, StringComparison.OrdinalIgnoreCase) && redirect.UserInfo.Length == 0,
			"http" => redirect.IsLoopback && redirect.UserInfo.Length == 0,
			var scheme => scheme.Equals(Reversed(host), StringComparison.OrdinalIgnoreCase)
				|| scheme.StartsWith(Reversed(host) + ".", StringComparison.OrdinalIgnoreCase),
		};
	}

	private static string Reversed(string host) => string.Join('.', host.Split('.').Reverse());

	/// <summary>
	/// Reads the document served at <paramref name="clientId"/> into the app it describes, not yet stored: its
	/// <c>client_id</c> must be that address, it must name at least one redirect address that
	/// <see cref="RedirectFits"/>, and it must be a public client (no secret, <c>token_endpoint_auth_method</c>
	/// <c>none</c> when given). <c>client_name</c> is shown to players (the host when it has none), <c>client_uri</c> on
	/// the same host is its homepage, and <c>scope</c> is the most it may ask for (<c>profile</c> when it has none).
	/// </summary>
	public static Result<OAuthClient> Read(string clientId, string json, DateTimeOffset now)
	{
		if (json.Length > MaxDocumentBytes) return Problem($"The description is larger than {MaxDocumentBytes} bytes.");
		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(json, Parsing);
		}
		catch (JsonException)
		{
			return Problem("The description is not JSON.");
		}

		using (document)
		{
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object) return Problem("The description is not a JSON object.");
			if (Text(root, "client_id") != clientId) return Problem("The description's client_id is not the address it was read from.");
			if (root.TryGetProperty("client_secret", out _) || root.TryGetProperty("client_secret_expires_at", out _))
				return Problem("A described app may not have a secret.");
			if (Text(root, "token_endpoint_auth_method") is { } method && method != "none")
				return Problem("A described app must be public: token_endpoint_auth_method must be none.");
			if (Strings(root, "grant_types") is { } grants && !grants.Contains("authorization_code"))
				return Problem("A described app must use the authorization_code grant.");
			if (Strings(root, "response_types") is { } responses && !responses.Contains("code"))
				return Problem("A described app must use response_type code.");

			if (Strings(root, "redirect_uris") is not { Count: > 0 and <= OAuthService.MaxRedirectUris } redirects)
				return Problem($"The description needs between 1 and {OAuthService.MaxRedirectUris} redirect_uris.");
			if (redirects.FirstOrDefault(uri => !RedirectFits(clientId, uri)) is { } bad)
				return Problem($"'{bad}' cannot be its redirect address: use https on {HostOf(clientId)}, http on 127.0.0.1 or [::1], or the scheme {Reversed(HostOf(clientId))}.");

			if (DelegatedScopes.Parse(Text(root, "scope")) is not { } scopes)
				return Problem("The description's scope is not a space-separated list of scopes.");

			var host = HostOf(clientId);
			var name = Text(root, "client_name") is { } given && Shown(given) is { Length: > 0 } shown ? shown : host;
			var homepage = Text(root, "client_uri") is { } site && Uri.TryCreate(site, UriKind.Absolute, out var page)
				&& page.Scheme == "https" && page.IdnHost.Equals(host, StringComparison.OrdinalIgnoreCase) && site.Length <= OAuthService.MaxUriLength
					? site
					: null;
			return new OAuthClient(clientId, name, null, homepage, redirects, scopes, null, now)
			{
				Status = OAuthClientStatus.Unreviewed,
				MetadataFetchedAt = now
			};
		}
	}

	/// <summary>A name as players see it: control and format characters dropped, spaces collapsed, at most <see cref="OAuthService.MaxNameLength"/>.</summary>
	private static string Shown(string name)
	{
		// Control and format characters (bidirectional overrides, zero-width spaces) could make a name read as another.
		var clean = string.Join(' ', new string([.. name.Where(c => !char.IsControl(c)
				&& char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format)])
			.Split(' ', StringSplitOptions.RemoveEmptyEntries));
		return clean.Length > OAuthService.MaxNameLength ? clean[..OAuthService.MaxNameLength] : clean;
	}

	private static string? Text(JsonElement root, string name)
		=> root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	private static List<string>? Strings(JsonElement root, string name)
		=> root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
			? [.. value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
			: null;

	private static Error<string> Problem(string message) => new(message);
}

/// <summary>
/// Reads the document an app's metadata <c>client_id</c> serves (<see cref="ClientIdMetadata"/>). The server's reader
/// goes only to public addresses, follows no redirect, and stops at <see cref="ClientIdMetadata.MaxDocumentBytes"/>.
/// </summary>
public interface IClientMetadataFetcher
{
	/// <summary>The document's text, or why it could not be read.</summary>
	ValueTask<Result<string>> FetchAsync(string clientId, CancellationToken ct = default);
}
