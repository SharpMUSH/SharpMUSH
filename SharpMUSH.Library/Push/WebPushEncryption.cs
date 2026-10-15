using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SharpMUSH.Library.Push;

/// <summary>
/// Encrypts a push message for one browser, as RFC 8291 says: the <c>aes128gcm</c> content coding (RFC 8188)
/// with a key agreed between a fresh key pair of ours and the browser's subscription key, mixed with the
/// subscription's authentication secret. The push service carries the result and cannot read it.
/// </summary>
public static class WebPushEncryption
{
	/// <summary>The record size written in the header. One record holds the whole message.</summary>
	public const int RecordSize = 4096;

	/// <summary>
	/// The most plaintext one record holds: the record size less the AES-GCM tag (16) and the padding
	/// delimiter (1). Push services accept a body of 4096 octets at least (RFC 8030), and the header takes 86 of
	/// those, so this is also the limit on what is sent.
	/// </summary>
	public const int MaxPlaintext = RecordSize - 86 - 16 - 1;

	private static readonly byte[] KeyInfoLabel = "WebPush: info\0"u8.ToArray();
	private static readonly byte[] CekInfo = "Content-Encoding: aes128gcm\0"u8.ToArray();
	private static readonly byte[] NonceInfo = "Content-Encoding: nonce\0"u8.ToArray();

	/// <summary>Encrypts <paramref name="plaintext"/> for a subscription with a fresh key pair and salt.</summary>
	/// <param name="plaintext">The message, at most <see cref="MaxPlaintext"/> bytes.</param>
	/// <param name="userAgentPublicKey">The subscription's <c>p256dh</c> key: an uncompressed P-256 point, 65 bytes.</param>
	/// <param name="authSecret">The subscription's <c>auth</c> secret, 16 bytes.</param>
	public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, byte[] userAgentPublicKey, byte[] authSecret)
	{
		using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
		return Encrypt(plaintext, userAgentPublicKey, authSecret, ephemeral, RandomNumberGenerator.GetBytes(16));
	}

	/// <summary>
	/// Encrypts with the key pair and salt given: what <see cref="Encrypt(ReadOnlySpan{byte}, byte[], byte[])"/> does
	/// with fresh ones. Given fixed ones it reproduces RFC 8291's worked example, which is how it is tested.
	/// </summary>
	public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, byte[] userAgentPublicKey, byte[] authSecret,
		ECDiffieHellman serverKey, byte[] salt)
	{
		if (plaintext.Length > MaxPlaintext)
		{
			throw new ArgumentOutOfRangeException(nameof(plaintext), $"A push message holds at most {MaxPlaintext} bytes.");
		}

		var serverPublic = VapidKeys.PublicPoint(serverKey.ExportParameters(false));
		var ecdhSecret = SharedSecret(serverKey, userAgentPublicKey);

		// HKDF-Extract(auth_secret, ecdh_secret), then expand with the two public keys: the input keying material.
		var keyInfo = Concat(KeyInfoLabel, userAgentPublicKey, serverPublic);
		var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdhSecret, 32, authSecret, keyInfo);

		var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
		var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, CekInfo);
		var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, NonceInfo);

		var padded = new byte[plaintext.Length + 1];
		plaintext.CopyTo(padded);
		padded[^1] = 2; // the last record's delimiter

		var header = Header(salt, serverPublic);
		var body = new byte[header.Length + padded.Length + 16];
		header.CopyTo(body, 0);
		using var aes = new AesGcm(cek, 16);
		aes.Encrypt(nonce, padded, body.AsSpan(header.Length, padded.Length), body.AsSpan(header.Length + padded.Length, 16));
		return body;
	}

	private static byte[] SharedSecret(ECDiffieHellman serverKey, byte[] userAgentPublicKey)
	{
		using var peer = ECDiffieHellman.Create(VapidKeys.FromPublicPoint(userAgentPublicKey));
		return serverKey.DeriveRawSecretAgreement(peer.PublicKey);
	}

	private static byte[] Header(byte[] salt, byte[] serverPublic)
	{
		var header = new byte[salt.Length + 4 + 1 + serverPublic.Length];
		salt.CopyTo(header, 0);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(salt.Length), RecordSize);
		header[salt.Length + 4] = (byte)serverPublic.Length;
		serverPublic.CopyTo(header, salt.Length + 5);
		return header;
	}

	private static byte[] Concat(params byte[][] parts)
	{
		var result = new byte[parts.Sum(part => part.Length)];
		var offset = 0;
		foreach (var part in parts)
		{
			part.CopyTo(result, offset);
			offset += part.Length;
		}

		return result;
	}
}
