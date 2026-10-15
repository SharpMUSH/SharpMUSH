using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SharpMUSH.Library.Push;

/// <summary>
/// The server's VAPID key (RFC 8292): a P-256 key pair whose public half a browser is given when it subscribes,
/// and whose private half signs every push request, so a push service delivers only what this server sends to
/// its subscriptions. Kept in the secret store as <see cref="SecretName"/>, a PKCS#8 private key in base64 (or PEM,
/// when an operator supplies one).
/// </summary>
public sealed class VapidKeys : IDisposable
{
	/// <summary>The secret holding the private key. Clearing it makes a new key, and every subscription made with the old one stops.</summary>
	public const string SecretName = "push.vapid";

	private readonly ECDsa _key;

	private VapidKeys(ECDsa key)
	{
		_key = key;
		PublicKey = PublicPoint(key.ExportParameters(false));
		PublicKeyText = Base64Url(PublicKey);
	}

	/// <summary>The public key as an uncompressed point, 65 bytes: what a browser subscribes with.</summary>
	public byte[] PublicKey { get; }

	/// <summary><see cref="PublicKey"/> in base64url, as the portal passes it to <c>pushManager.subscribe()</c>.</summary>
	public string PublicKeyText { get; }

	/// <summary>A new key pair.</summary>
	public static VapidKeys Generate() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

	/// <summary>The key pair a stored secret holds, or null when it is not a P-256 private key.</summary>
	public static VapidKeys? Parse(string stored)
	{
		var key = ECDsa.Create();
		try
		{
			var text = stored.Trim();
			if (text.StartsWith("-----", StringComparison.Ordinal))
			{
				key.ImportFromPem(text);
			}
			else
			{
				key.ImportPkcs8PrivateKey(Convert.FromBase64String(text), out _);
			}

			if (key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
			{
				key.Dispose();
				return null;
			}

			return new VapidKeys(key);
		}
		catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException)
		{
			key.Dispose();
			return null;
		}
	}

	/// <summary>The private key as the secret store keeps it.</summary>
	public string Export() => Convert.ToBase64String(_key.ExportPkcs8PrivateKey());

	/// <summary>
	/// The <c>Authorization</c> header value for a push to <paramref name="endpoint"/>: a JWT for the endpoint's
	/// origin, signed with ES256, and the public key it is checked against.
	/// </summary>
	/// <param name="endpoint">The subscription's endpoint.</param>
	/// <param name="subject">A <c>mailto:</c> or <c>https:</c> URL the push service can reach the game's operator at.</param>
	/// <param name="expires">When the token stops being valid; push services refuse more than 24 hours ahead.</param>
	public string Authorization(Uri endpoint, string subject, DateTimeOffset expires)
	{
		var header = Base64Url("""{"typ":"JWT","alg":"ES256"}"""u8);
		var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
		{
			["aud"] = endpoint.GetLeftPart(UriPartial.Authority),
			["exp"] = expires.ToUnixTimeSeconds(),
			["sub"] = subject
		}));
		var signingInput = $"{header}.{claims}";
		var signature = _key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256,
			DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
		return $"vapid t={signingInput}.{Base64Url(signature)}, k={PublicKeyText}";
	}

	/// <summary>The uncompressed point (<c>0x04 || X || Y</c>) of a P-256 public key.</summary>
	public static byte[] PublicPoint(ECParameters parameters)
	{
		var point = new byte[65];
		point[0] = 4;
		parameters.Q.X!.CopyTo(point, 1);
		parameters.Q.Y!.CopyTo(point, 33);
		return point;
	}

	/// <summary>The public key an uncompressed P-256 point holds. Throws when it is not 65 bytes starting 0x04.</summary>
	public static ECParameters FromPublicPoint(byte[] point)
	{
		if (point.Length != 65 || point[0] != 4)
		{
			throw new CryptographicException("Not an uncompressed P-256 point.");
		}

		return new ECParameters
		{
			Curve = ECCurve.NamedCurves.nistP256,
			Q = new ECPoint { X = point[1..33], Y = point[33..] }
		};
	}

	/// <summary>Base64url without padding.</summary>
	public static string Base64Url(ReadOnlySpan<byte> bytes) => System.Buffers.Text.Base64Url.EncodeToString(bytes);

	public void Dispose() => _key.Dispose();
}
