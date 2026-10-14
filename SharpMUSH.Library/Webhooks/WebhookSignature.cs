using System.Security.Cryptography;
using System.Text;

namespace SharpMUSH.Library.Webhooks;

/// <summary>
/// Signs a delivery the way the Standard Webhooks specification does, so a receiver can check it with
/// any of that specification's libraries: an HMAC-SHA256 over <c>{id}.{timestamp}.{body}</c>, keyed with
/// the subscription's secret, sent as <c>webhook-signature: v1,&lt;base64&gt;</c> beside
/// <c>webhook-id</c> and <c>webhook-timestamp</c>.
/// </summary>
public static class WebhookSignature
{
	/// <summary>What every secret starts with; the rest is the key in base64.</summary>
	public const string SecretPrefix = "whsec_";

	public const string IdHeader = "webhook-id";
	public const string TimestampHeader = "webhook-timestamp";
	public const string SignatureHeader = "webhook-signature";

	/// <summary>A new secret: 32 random bytes.</summary>
	public static string NewSecret() => SecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

	/// <summary>The <c>webhook-signature</c> value for one delivery attempt.</summary>
	/// <param name="secret">The subscription's secret, with or without its <c>whsec_</c> prefix.</param>
	/// <param name="messageId">The delivery's id, the same on every attempt.</param>
	/// <param name="timestamp">The attempt's time, in Unix seconds.</param>
	/// <param name="body">The body exactly as sent.</param>
	public static string Sign(string secret, string messageId, long timestamp, string body)
	{
		var key = Convert.FromBase64String(secret.StartsWith(SecretPrefix, StringComparison.Ordinal) ? secret[SecretPrefix.Length..] : secret);
		var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{messageId}.{timestamp}.{body}"));
		return "v1," + Convert.ToBase64String(mac);
	}

	/// <summary>Whether <paramref name="presented"/> is <paramref name="secret"/>, compared in constant time.</summary>
	public static bool SecretEquals(string secret, string presented)
		=> CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(presented));
}
