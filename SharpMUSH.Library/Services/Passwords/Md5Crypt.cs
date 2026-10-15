using System.Security.Cryptography;
using System.Text;

namespace SharpMUSH.Library.Services.Passwords;

/// <summary>
/// MD5-crypt (<c>$1$</c>), Poul-Henning Kamp's FreeBSD <c>crypt(3)</c> scheme that glibc, libxcrypt and
/// <c>openssl passwd -1</c> also write. TinyMUX and RhostMUSH accept passwords stored this way, as a game's
/// <c>crypt(3)</c> made them, so a player carried over from such a game keeps theirs.
/// </summary>
/// <remarks>
/// A stored value is <c>$1$</c>, a salt of up to eight characters, <c>$</c>, and 22 characters of encoded
/// hash. Verification computes the whole string again and compares it with the stored one.
/// </remarks>
internal static class Md5Crypt
{
	private const string Magic = "$1$";
	private const int MaxSaltLength = 8;
	private const string Alphabet = "./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

	/// <summary>Whether <paramref name="stored"/> is an MD5-crypt value.</summary>
	public static bool IsMd5Crypt(string stored) => stored.StartsWith(Magic, StringComparison.Ordinal);

	/// <summary>Whether <paramref name="password"/> is the password <paramref name="stored"/> was made from.</summary>
	public static bool Verify(string stored, ReadOnlySpan<byte> password)
	{
		if (!IsMd5Crypt(stored))
		{
			return false;
		}

		var rest = stored.AsSpan(Magic.Length);
		var saltEnd = rest.IndexOf('$');
		if (saltEnd < 0 || saltEnd > MaxSaltLength)
		{
			return false;
		}

		var computed = Crypt(password, Encoding.ASCII.GetBytes(rest[..saltEnd].ToString()));
		return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(stored));
	}

	/// <summary>The complete crypt string for <paramref name="password"/> under <paramref name="salt"/> (at most 8 bytes).</summary>
	public static string Crypt(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt)
	{
		salt = salt[..Math.Min(salt.Length, MaxSaltLength)];
		var magic = Encoding.ASCII.GetBytes(Magic);

		using var alternate = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
		alternate.AppendData(password);
		alternate.AppendData(salt);
		alternate.AppendData(password);
		var final = alternate.GetHashAndReset();

		using var context = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
		context.AppendData(password);
		context.AppendData(magic);
		context.AppendData(salt);
		for (var left = password.Length; left > 0; left -= 16)
		{
			context.AppendData(final.AsSpan(0, Math.Min(left, 16)));
		}

		// The original reads its zeroed buffer here, not the digest: a NUL for each set bit, else the
		// password's first byte.
		Span<byte> zero = [0];
		for (var bits = password.Length; bits != 0; bits >>= 1)
		{
			context.AppendData((bits & 1) != 0 ? zero : password[..1]);
		}

		final = context.GetHashAndReset();

		for (var round = 0; round < 1000; round++)
		{
			if ((round & 1) != 0) context.AppendData(password);
			else context.AppendData(final);

			if (round % 3 != 0) context.AppendData(salt);
			if (round % 7 != 0) context.AppendData(password);

			if ((round & 1) != 0) context.AppendData(final);
			else context.AppendData(password);

			final = context.GetHashAndReset();
		}

		var result = new StringBuilder(Magic.Length + salt.Length + 1 + 22);
		result.Append(Magic).Append(Encoding.ASCII.GetString(salt)).Append('$');
		Encode(result, final[0], final[6], final[12], 4);
		Encode(result, final[1], final[7], final[13], 4);
		Encode(result, final[2], final[8], final[14], 4);
		Encode(result, final[3], final[9], final[15], 4);
		Encode(result, final[4], final[10], final[5], 4);
		Encode(result, 0, 0, final[11], 2);
		CryptographicOperations.ZeroMemory(final);
		return result.ToString();
	}

	private static void Encode(StringBuilder output, byte high, byte middle, byte low, int characters)
	{
		var value = (high << 16) | (middle << 8) | low;
		for (var i = 0; i < characters; i++)
		{
			output.Append(Alphabet[value & 0x3f]);
			value >>= 6;
		}
	}
}
