using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SharpMUSH.Library.Services.Passwords;

/// <summary>
/// SHA-256-crypt (<c>$5$</c>) and SHA-512-crypt (<c>$6$</c>), the <c>crypt(3)</c> schemes glibc and
/// libxcrypt implement, following Ulrich Drepper's public-domain specification "Unix crypt using
/// SHA-256 and SHA-512". RhostMUSH stores its passwords this way, and so can TinyMUX.
/// </summary>
/// <remarks>
/// A stored value is <c>$5$</c> or <c>$6$</c>, optionally <c>rounds=&lt;N&gt;$</c>, a salt of up to 16
/// characters, <c>$</c>, and the encoded hash. Verification computes the whole string again from the
/// stored parameters and compares it with the stored one, so a value no implementation could have
/// written (a rounds count outside 1000-999999999, a salt over 16 characters) does not match.
/// </remarks>
internal static class ShaCrypt
{
	private const int DefaultRounds = 5000;
	private const int MinRounds = 1000;
	private const int MaxRounds = 999_999_999;
	private const int MaxSaltLength = 16;
	private const string RoundsPrefix = "rounds=";
	private const string Alphabet = "./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

	/// <summary>Whether <paramref name="stored"/> is a SHA-crypt value (<c>$5$</c> or <c>$6$</c>).</summary>
	public static bool IsShaCrypt(string stored)
		=> stored.StartsWith("$5$", StringComparison.Ordinal) || stored.StartsWith("$6$", StringComparison.Ordinal);

	/// <summary>Whether <paramref name="password"/> is the password <paramref name="stored"/> was made from.</summary>
	public static bool Verify(string stored, ReadOnlySpan<byte> password)
	{
		if (!IsShaCrypt(stored))
		{
			return false;
		}

		var rest = stored.AsSpan(3);
		int? rounds = null;
		if (rest.StartsWith(RoundsPrefix, StringComparison.Ordinal))
		{
			var end = rest.IndexOf('$');
			if (end < 0 || !int.TryParse(rest[RoundsPrefix.Length..end], NumberStyles.None, CultureInfo.InvariantCulture,
					out var parsed))
			{
				return false;
			}

			rounds = parsed;
			rest = rest[(end + 1)..];
		}

		var saltEnd = rest.IndexOf('$');
		if (saltEnd < 0 || rounds is < MinRounds or > MaxRounds)
		{
			return false;
		}

		var salt = rest[..saltEnd];
		if (salt.Length > MaxSaltLength || salt.ContainsAnyExceptInRange((char)0x21, (char)0x7e))
		{
			return false;
		}

		var computed = Crypt(stored[1] == '6' ? HashAlgorithmName.SHA512 : HashAlgorithmName.SHA256, password,
			Encoding.ASCII.GetBytes(salt.ToString()), rounds);
		return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(stored));
	}

	/// <summary>
	/// The complete crypt string for <paramref name="password"/> under <paramref name="salt"/> (at most
	/// 16 bytes) and <paramref name="rounds"/>; <see langword="null"/> rounds is the default 5000 and
	/// leaves <c>rounds=</c> out of the result, as the specification does.
	/// </summary>
	public static string Crypt(HashAlgorithmName algorithm, ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt,
		int? rounds)
	{
		var count = rounds ?? DefaultRounds;
		var size = algorithm == HashAlgorithmName.SHA512 ? 64 : 32;

		var a = new byte[size];
		var b = new byte[size];
		var c = new byte[size];
		var dp = new byte[size];
		var ds = new byte[size];
		var p = ArrayPool<byte>.Shared.Rent(Math.Max(password.Length, 1));
		var s = new byte[salt.Length];
		try
		{
			using var hash = IncrementalHash.CreateHash(algorithm);

			// Digest B: password, salt, password.
			hash.AppendData(password);
			hash.AppendData(salt);
			hash.AppendData(password);
			hash.GetHashAndReset(b);

			// Digest A: password, salt, then B once for each full digest length of the password and the
			// first bytes of B for what is left over.
			hash.AppendData(password);
			hash.AppendData(salt);
			AppendRepeated(hash, b, password.Length);

			// Then, for each bit of the password's length from the lowest, B for a one and the password
			// for a zero.
			for (var length = password.Length; length > 0; length >>= 1)
			{
				if ((length & 1) != 0)
				{
					hash.AppendData(b);
				}
				else
				{
					hash.AppendData(password);
				}
			}

			hash.GetHashAndReset(a);

			// Digest DP: the password once for each of its bytes; P is that digest repeated to the
			// password's length.
			for (var i = 0; i < password.Length; i++)
			{
				hash.AppendData(password);
			}

			hash.GetHashAndReset(dp);
			var pSpan = p.AsSpan(0, password.Length);
			Fill(pSpan, dp);

			// Digest DS: the salt 16 + A[0] times; S is that digest repeated to the salt's length.
			for (var i = 0; i < 16 + a[0]; i++)
			{
				hash.AppendData(salt);
			}

			hash.GetHashAndReset(ds);
			Fill(s, ds);

			// The rounds, each starting from the last one's digest.
			a.CopyTo(c, 0);
			for (var round = 0; round < count; round++)
			{
				var odd = (round & 1) != 0;
				if (odd)
				{
					hash.AppendData(pSpan);
				}
				else
				{
					hash.AppendData(c);
				}

				if (round % 3 != 0)
				{
					hash.AppendData(s);
				}

				if (round % 7 != 0)
				{
					hash.AppendData(pSpan);
				}

				if (odd)
				{
					hash.AppendData(c);
				}
				else
				{
					hash.AppendData(pSpan);
				}

				hash.GetHashAndReset(c);
			}

			var result = new StringBuilder(size == 64 ? 123 : 80);
			result.Append(size == 64 ? "$6$" : "$5$");
			if (rounds is not null)
			{
				result.Append(CultureInfo.InvariantCulture, $"{RoundsPrefix}{count}$");
			}

			result.Append(Encoding.ASCII.GetString(salt)).Append('$');
			Encode(result, c, size == 64 ? Sha512Order : Sha256Order);
			return result.ToString();
		}
		finally
		{
			CryptographicOperations.ZeroMemory(a);
			CryptographicOperations.ZeroMemory(b);
			CryptographicOperations.ZeroMemory(c);
			CryptographicOperations.ZeroMemory(dp);
			CryptographicOperations.ZeroMemory(ds);
			CryptographicOperations.ZeroMemory(p.AsSpan(0, password.Length));
			ArrayPool<byte>.Shared.Return(p);
		}
	}

	private static void AppendRepeated(IncrementalHash hash, byte[] digest, int length)
	{
		for (; length > digest.Length; length -= digest.Length)
		{
			hash.AppendData(digest);
		}

		hash.AppendData(digest.AsSpan(0, length));
	}

	private static void Fill(Span<byte> target, ReadOnlySpan<byte> digest)
	{
		for (var offset = 0; offset < target.Length; offset += digest.Length)
		{
			var chunk = Math.Min(digest.Length, target.Length - offset);
			digest[..chunk].CopyTo(target[offset..]);
		}
	}

	/// <summary>
	/// The final digest's bytes, three at a time in the specification's order, each group written as
	/// four characters of <see cref="Alphabet"/>, least significant six bits first. The last group is
	/// short: one byte (two characters) for SHA-512, two (three characters) for SHA-256.
	/// </summary>
	private static void Encode(StringBuilder output, byte[] digest, (int, int, int, int)[] order)
	{
		foreach (var (high, middle, low, characters) in order)
		{
			var word = (high < 0 ? 0 : digest[high] << 16) | (middle < 0 ? 0 : digest[middle] << 8) | digest[low];
			for (var i = 0; i < characters; i++)
			{
				output.Append(Alphabet[word & 0x3f]);
				word >>= 6;
			}
		}
	}

	private static readonly (int, int, int, int)[] Sha512Order =
	[
		(0, 21, 42, 4), (22, 43, 1, 4), (44, 2, 23, 4), (3, 24, 45, 4), (25, 46, 4, 4), (47, 5, 26, 4),
		(6, 27, 48, 4), (28, 49, 7, 4), (50, 8, 29, 4), (9, 30, 51, 4), (31, 52, 10, 4), (53, 11, 32, 4),
		(12, 33, 54, 4), (34, 55, 13, 4), (56, 14, 35, 4), (15, 36, 57, 4), (37, 58, 16, 4), (59, 17, 38, 4),
		(18, 39, 60, 4), (40, 61, 19, 4), (62, 20, 41, 4), (-1, -1, 63, 2)
	];

	private static readonly (int, int, int, int)[] Sha256Order =
	[
		(0, 10, 20, 4), (21, 1, 11, 4), (12, 22, 2, 4), (3, 13, 23, 4), (24, 4, 14, 4), (15, 25, 5, 4),
		(6, 16, 26, 4), (27, 7, 17, 4), (18, 28, 8, 4), (9, 19, 29, 4), (-1, 31, 30, 3)
	];
}
