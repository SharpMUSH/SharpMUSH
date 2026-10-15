using System.Security.Cryptography;
using System.Text;
using SharpMUSH.Library.Secrets;

namespace SharpMUSH.Library.Push;

/// <summary>
/// Which push endpoints the server sends to. A subscription's endpoint comes from the browser, so taking any URL
/// would let anyone signed in make the server POST wherever they liked. Only HTTPS on the browsers' own push
/// services is accepted, plus whatever the operator adds (<c>WebPush:AdditionalHosts</c>) for a browser this
/// list does not know.
/// </summary>
public static class PushEndpoints
{
	/// <summary>
	/// The push services of the browsers in use: Chrome, Edge's Chromium push and other Chromium browsers (FCM),
	/// Firefox (Mozilla autopush), Safari (Apple) and Edge on Windows (WNS). Patterns as <see cref="SecretHosts"/>
	/// reads them.
	/// </summary>
	public static readonly IReadOnlyList<string> KnownHosts =
	[
		"fcm.googleapis.com",
		"updates.push.services.mozilla.com",
		"web.push.apple.com",
		"*.push.apple.com",
		"*.notify.windows.com"
	];

	/// <summary>
	/// Whether <paramref name="endpoint"/> is an HTTPS URL on a known push service or on one of
	/// <paramref name="additionalHosts"/>, with no user name or password.
	/// </summary>
	public static bool IsAllowed(string endpoint, IEnumerable<string> additionalHosts)
		=> endpoint.Length <= 2048
			&& Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
			&& uri.Scheme == Uri.UriSchemeHttps
			&& uri.UserInfo.Length == 0
			&& SecretHosts.Allows(KnownHosts.Concat(additionalHosts.Select(host => host.Trim().ToLowerInvariant())), uri.IdnHost);

	/// <summary>A subscription's id: the SHA-256 of its endpoint, base64url.</summary>
	public static string IdOf(string endpoint)
		=> System.Buffers.Text.Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));

	/// <summary>Which VAPID key a subscription was made with: the first 12 bytes of the SHA-256 of its public key, base64url.</summary>
	public static string KeyIdOf(byte[] publicKey)
		=> System.Buffers.Text.Base64Url.EncodeToString(SHA256.HashData(publicKey).AsSpan(0, 12));
}
