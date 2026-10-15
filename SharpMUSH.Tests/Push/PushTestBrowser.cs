using System.Security.Cryptography;
using SharpMUSH.Library.Push;

namespace SharpMUSH.Tests.Push;

/// <summary>A browser's side of web push, for tests: a subscription's keys, and decrypting what arrives.</summary>
internal static class PushTestBrowser
{
	/// <summary>What a browser does with a push (RFC 8291 section 3.4, then RFC 8188): the plaintext without its padding.</summary>
	public static byte[] Decrypt(byte[] body, ECDiffieHellman browser, byte[] browserPublic, byte[] auth)
	{
		var salt = body[..16];
		var idLength = body[20];
		var serverPublic = body[21..(21 + idLength)];
		var cipher = body[(21 + idLength)..];

		using var server = ECDiffieHellman.Create(VapidKeys.FromPublicPoint(serverPublic));
		var secret = browser.DeriveRawSecretAgreement(server.PublicKey);
		var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, auth,
			[.. "WebPush: info\0"u8, .. browserPublic, .. serverPublic]);
		var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
		var key = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, "Content-Encoding: aes128gcm\0"u8.ToArray());
		var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, "Content-Encoding: nonce\0"u8.ToArray());

		var plain = new byte[cipher.Length - 16];
		using var aes = new AesGcm(key, 16);
		aes.Decrypt(nonce, cipher[..^16], cipher[^16..], plain);
		var end = Array.LastIndexOf(plain, (byte)2);
		return plain[..end];
	}
}
