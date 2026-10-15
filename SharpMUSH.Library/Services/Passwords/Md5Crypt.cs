using System.Security.Cryptography;
using System.Text;

namespace SharpMUSH.Library.Services.Passwords;

/// <summary>
/// The MD5-based <c>crypt(3)</c> of FreeBSD and glibc, <c>$1$SALT$HASH</c> (Poul-Henning Kamp's
/// md5crypt): what TinyMUX stores a password as when its <c>crypt(3)</c> chose MD5, so a player carried
/// over from such a game logs in with the same password.
/// </summary>
/// <remarks>
/// The salt is at most eight characters, up to the next <c>$</c>. The digest of password, <c>$1$</c> and
/// salt is mixed with a second digest of password, salt and password, then rehashed 1000 times, and the
/// sixteen bytes are written in crypt's base-64 alphabet in md5crypt's byte order.
/// </remarks>
internal static class Md5Crypt
{
	private const string Magic = "$1$";
	private const string Alphabet = "./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

	/// <summary>Whether <paramref name="stored"/> is an md5crypt value that <paramref name="attempt"/> produces.</summary>
	public static bool Verify(string stored, ReadOnlySpan<byte> attempt)
	{
		if (!stored.StartsWith(Magic, StringComparison.Ordinal)) return false;
		var end = stored.IndexOf('$', Magic.Length);
		if (end < 0) return false;

		var salt = stored[Magic.Length..Math.Min(end, Magic.Length + 8)];
		var expected = Encoding.ASCII.GetBytes(Crypt(attempt, salt));
		var actual = Encoding.ASCII.GetBytes(stored);
		return CryptographicOperations.FixedTimeEquals(expected, actual);
	}

	/// <summary><c>crypt(password, "$1$" + salt)</c>, the whole stored form.</summary>
	public static string Crypt(ReadOnlySpan<byte> password, string salt)
	{
		var saltBytes = Encoding.ASCII.GetBytes(salt);

		using var alternate = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
		alternate.AppendData(password);
		alternate.AppendData(saltBytes);
		alternate.AppendData(password);
		var alternateDigest = alternate.GetHashAndReset();

		using var context = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
		context.AppendData(password);
		context.AppendData(Encoding.ASCII.GetBytes(Magic));
		context.AppendData(saltBytes);
		for (var remaining = password.Length; remaining > 0; remaining -= 16)
		{
			context.AppendData(alternateDigest.AsSpan(0, Math.Min(16, remaining)));
		}

		for (var bits = password.Length; bits != 0; bits >>= 1)
		{
			if ((bits & 1) != 0)
			{
				context.AppendData([0]);
			}
			else
			{
				context.AppendData(password[..Math.Min(1, password.Length)]);
			}
		}

		var digest = context.GetHashAndReset();
		for (var round = 0; round < 1000; round++)
		{
			if ((round & 1) != 0) context.AppendData(password);
			else context.AppendData(digest);
			if (round % 3 != 0) context.AppendData(saltBytes);
			if (round % 7 != 0) context.AppendData(password);
			if ((round & 1) != 0) context.AppendData(digest);
			else context.AppendData(password);
			digest = context.GetHashAndReset();
		}

		var output = new StringBuilder(Magic.Length + salt.Length + 23);
		output.Append(Magic).Append(salt).Append('$');
		Encode(output, (digest[0] << 16) | (digest[6] << 8) | digest[12], 4);
		Encode(output, (digest[1] << 16) | (digest[7] << 8) | digest[13], 4);
		Encode(output, (digest[2] << 16) | (digest[8] << 8) | digest[14], 4);
		Encode(output, (digest[3] << 16) | (digest[9] << 8) | digest[15], 4);
		Encode(output, (digest[4] << 16) | (digest[10] << 8) | digest[5], 4);
		Encode(output, digest[11], 2);
		CryptographicOperations.ZeroMemory(digest);
		CryptographicOperations.ZeroMemory(alternateDigest);
		return output.ToString();
	}

	private static void Encode(StringBuilder output, int value, int count)
	{
		for (var i = 0; i < count; i++)
		{
			output.Append(Alphabet[value & 0x3f]);
			value >>= 6;
		}
	}
}
