using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Services.Passwords;

/// <summary>
/// Verifies a password against a value an imported PennMUSH database stored, accepting what
/// PennMUSH's <c>password_check</c> (src/player.c) accepts, in its order.
/// </summary>
/// <remarks>
/// <list type="number">
///   <item>The formatted string <c>V:ALGO:HASH:TIMESTAMP</c> (<c>password_comp</c>, src/mycrypt.c):
///   version 1 is the hex digest of the password, version 2 a two-character salt followed by the hex
///   digest of salt + password. ALGO is any digest PennMUSH could name; 1.8.8 defaults to sha512.</item>
///   <item>An old SHA-0 password: <c>XX</c> and the first two 32-bit words of SHA-0(password) in
///   decimal (<c>mush_crypt_sha0</c>).</item>
///   <item><c>crypt(password, "XX")</c>.</item>
///   <item>SHA-crypt, <c>$6$[rounds=N$]SALT$HASH</c> or <c>$5$...</c> (<see cref="ShaCrypt"/>): not
///   PennMUSH's, but what RhostMUSH and glibc's <c>crypt(3)</c> write, so a password carried over from
///   such a game verifies too.</item>
///   <item>MD5-crypt, <c>$1$SALT$HASH</c> (<see cref="Md5Crypt"/>): a TinyMUX or RhostMUSH game's
///   <c>crypt(3)</c> may have written it.</item>
///   <item>BSDi extended DES, <c>_CCCCSSSSHHHHHHHHHHH</c>: what a TinyMUX game's <c>crypt(3)</c> may have
///   written.</item>
///   <item>A TinyMUX password, <c>$ALGO$SALT$HASH</c> (<c>check_mux_password</c>).</item>
///   <item>Plaintext.</item>
/// </list>
/// Every buffer that holds the attempted password's bytes is zeroed before it is released.
/// </remarks>
internal static partial class PennMUSHPasswords
{
	public static bool Verify(string stored, string attempt)
	{
		var length = Encoding.UTF8.GetByteCount(attempt);
		var rented = ArrayPool<byte>.Shared.Rent(length);
		try
		{
			var bytes = rented.AsSpan(0, Encoding.UTF8.GetBytes(attempt, rented));
			return MatchesFormatted(stored, bytes)
				|| MatchesSha0(stored, bytes)
				|| MatchesCrypt(stored, bytes)
				|| ShaCrypt.Verify(stored, bytes)
				|| Md5Crypt.Verify(stored, bytes)
				|| MatchesExtendedDes(stored, bytes)
				|| MatchesMux(stored, bytes)
				|| MatchesPlaintext(stored, bytes);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(rented.AsSpan(0, length));
			ArrayPool<byte>.Shared.Return(rented);
		}
	}

	/// <summary>
	/// The pattern <c>password_comp</c> requires, in PennMUSH's own ASCII terms, with two leniencies
	/// that cost nothing because the password must still match: the timestamp may be missing, and
	/// the algorithm may be spelled with a hyphen (<c>SHA-256</c>).
	/// </summary>
	[GeneratedRegex("^([0-9]+):([A-Za-z0-9_-]+):([0-9A-Za-z]+)(?::[0-9]+)?")]
	private static partial Regex FormattedPattern();

	private static bool MatchesFormatted(string stored, ReadOnlySpan<byte> attempt)
	{
		var match = FormattedPattern().Match(stored);
		if (!match.Success)
		{
			return false;
		}

		var algorithm = match.Groups[2].Value;
		var hash = match.Groups[3].Value;
		switch (match.Groups[1].Value)
		{
			case "1":
				return Digest(algorithm, [], attempt) is { } unsalted
					&& HexEquals(unsalted, hash);

			case "2" when hash.Length >= 2:
				return Digest(algorithm, Encoding.ASCII.GetBytes(hash[..2]), attempt) is { } salted
					&& HexEquals(salted, hash[2..]);

			default:
				return false;
		}
	}

	/// <summary>
	/// <c>mush_crypt_sha0</c> copies the first eight digest bytes into two native integers, so its
	/// output depends on the host's byte order, and the <c>reverse_shs</c> option swaps them for a
	/// database moved between hosts. The source database records neither, so either order matches.
	/// </summary>
	private static bool MatchesSha0(string stored, ReadOnlySpan<byte> attempt)
	{
		if (!stored.StartsWith("XX", StringComparison.Ordinal) || stored.Length == 2
			|| stored.AsSpan(2).ContainsAnyExceptInRange('0', '9'))
		{
			return false;
		}

		var digest = Sha0.HashData(attempt);
		var littleEndian = $"XX{BinaryPrimitives.ReadUInt32LittleEndian(digest)}{BinaryPrimitives.ReadUInt32LittleEndian(digest.AsSpan(4))}";
		var bigEndian = $"XX{BinaryPrimitives.ReadUInt32BigEndian(digest)}{BinaryPrimitives.ReadUInt32BigEndian(digest.AsSpan(4))}";
		return AsciiEquals(littleEndian, stored) | AsciiEquals(bigEndian, stored);
	}

	/// <summary>Whether the value has extended DES's shape: <c>_</c> and nineteen crypt-alphabet characters.</summary>
	private static bool IsExtendedDes(string stored)
		=> stored.Length == 20 && stored[0] == '_'
			&& !stored.AsSpan(1).ContainsAnyExcept(CryptAlphabet);

	private static bool MatchesExtendedDes(string stored, ReadOnlySpan<byte> attempt)
		=> IsExtendedDes(stored) && AsciiEquals(DesCrypt.ExtendedCrypt(attempt, stored[..9]), stored);

	/// <summary>
	/// <c>crypt(password, salt)</c>. PennMUSH always salts with <c>XX</c>; a TinyMUX built with
	/// <c>ENABLE_PROPER_DES</c> chose a random salt, which is the stored value's first two characters.
	/// </summary>
	private static bool MatchesCrypt(string stored, ReadOnlySpan<byte> attempt)
		=> IsTraditionalDes(stored)
			&& AsciiEquals(DesCrypt.Crypt(attempt, stored[..2]), stored);

	/// <summary>Whether the value has traditional DES's shape: thirteen crypt-alphabet characters.</summary>
	private static bool IsTraditionalDes(string stored)
		=> stored.Length == 13 && !stored.AsSpan().ContainsAnyExcept(CryptAlphabet);

	private const string CryptAlphabet = "./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

	/// <summary>
	/// <c>$ALGO$SALT$HASH</c>. PennMUSH digests the salt as the base64 text it is stored as, followed
	/// by the password, and compares that with the base64-decoded hash.
	/// </summary>
	private static bool MatchesMux(string stored, ReadOnlySpan<byte> attempt)
	{
		if (!stored.StartsWith('$') || ShaCrypt.IsShaCrypt(stored) || Md5Crypt.IsMd5Crypt(stored))
		{
			return false;
		}

		var fields = stored.Split('$', 4);
		if (fields.Length != 4)
		{
			return false;
		}

		var (algorithm, salt, hash) = (fields[1], fields[2], fields[3]);
		var expected = new byte[hash.Length];
		return Convert.TryFromBase64String(hash, expected, out var expectedLength)
			&& Digest(algorithm, Encoding.ASCII.GetBytes(salt), attempt) is { } digest
			&& CryptographicOperations.FixedTimeEquals(digest, expected.AsSpan(0, expectedLength));
	}

	/// <summary>
	/// PennMUSH's last resort, and only for an attempt that does not look encrypted: at least four
	/// bytes, not starting with '$' or "XX".
	/// </summary>
	/// <remarks>
	/// Stricter than PennMUSH in one respect. PennMUSH compares a plaintext attempt against any stored
	/// value, so typing a player's stored <c>V:ALGO:HASH:TIMESTAMP</c> string logs in as them. Here a
	/// value shaped like one of the hashes above is never compared as plaintext, so knowing a hash is
	/// not knowing the password.
	/// </remarks>
	private static bool MatchesPlaintext(string stored, ReadOnlySpan<byte> attempt)
	{
		if (attempt.Length < 4 || attempt[0] == (byte)'$' || attempt.StartsWith("XX"u8))
		{
			return false;
		}

		if (FormattedPattern().IsMatch(stored) || stored.StartsWith("XX", StringComparison.Ordinal)
			|| stored.StartsWith('$') || IsExtendedDes(stored) || IsTraditionalDes(stored))
		{
			return false;
		}

		// The stored value is the password itself, so its bytes get the same care as the attempt's.
		var length = Encoding.UTF8.GetByteCount(stored);
		var rented = ArrayPool<byte>.Shared.Rent(length);
		try
		{
			var bytes = rented.AsSpan(0, Encoding.UTF8.GetBytes(stored, rented));
			return CryptographicOperations.FixedTimeEquals(bytes, attempt);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(rented.AsSpan(0, length));
			ArrayPool<byte>.Shared.Return(rented);
		}
	}

	/// <summary>
	/// The digest of <paramref name="salt"/> followed by <paramref name="attempt"/>, for the digests
	/// PennMUSH looks up by name through OpenSSL, which ignores case; <c>null</c> for any other.
	/// </summary>
	private static byte[]? Digest(string algorithm, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> attempt)
	{
		HashAlgorithmName? name = algorithm.ToLowerInvariant() switch
		{
			"md5" => HashAlgorithmName.MD5,
			"sha1" or "sha-1" => HashAlgorithmName.SHA1,
			"sha256" or "sha-256" => HashAlgorithmName.SHA256,
			"sha384" or "sha-384" => HashAlgorithmName.SHA384,
			"sha512" or "sha-512" => HashAlgorithmName.SHA512,
			_ => null
		};
		if (name is null)
		{
			return null;
		}

		using var hash = IncrementalHash.CreateHash(name.Value);
		hash.AppendData(salt);
		hash.AppendData(attempt);
		return hash.GetHashAndReset();
	}

	/// <summary>PennMUSH writes lowercase hex; another writer's uppercase is the same digest.</summary>
	private static bool HexEquals(byte[] digest, string storedHex)
		=> AsciiEquals(Convert.ToHexStringLower(digest), storedHex.ToLowerInvariant());

	private static bool AsciiEquals(string computed, string stored)
		=> CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(stored));
}
