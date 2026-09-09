using Mediator;
using Microsoft.AspNetCore.Identity;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Password service that supports both modern PBKDF2 hashes and legacy PennMUSH SHA1 passwords.
/// 
/// PennMUSH password format: V:ALGO:HASH:TIMESTAMP
/// - V: Version number (currently 2)
/// - ALGO: Digest algorithm (SHA1 for PennMUSH)
/// - HASH: Salted hash (first 2 characters are the salt, prepended to plaintext before hashing)
/// - TIMESTAMP: Unix timestamp when password was set
/// 
/// The salt characters are from: abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789
/// 
/// When verifying, if the hash is in PennMUSH format, SHA1 verification is used.
/// New passwords are always hashed using the modern PBKDF2 algorithm.
/// </summary>
public class PasswordService(IMediator mediator, PasswordHasher<string> hasher) : IPasswordService
{
	private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

	public string GenerateRandomPassword()
		=> RandomNumberGenerator.GetString(Chars, 32);

	public string HashPassword(string user, string pw) =>
		hasher.HashPassword(user, pw);

	public bool PasswordIsValid(string user, string pw, string hash)
	{
		if (string.IsNullOrEmpty(hash))
			return false;

		if (TryParsePennMUSHHash(hash, out var algo, out var saltedHash))
		{
			return VerifyPennMUSHPassword(pw, algo, saltedHash);
		}

		try
		{
			return hasher.VerifyHashedPassword(user, hash, pw) != PasswordVerificationResult.Failed;
		}
		catch (FormatException)
		{
			// Invalid hash format (not a valid Base-64 string)
			return false;
		}
	}

	/// <summary>
	/// Determines if a password hash is in PennMUSH format.
	/// PennMUSH format: V:ALGO:HASH:TIMESTAMP (e.g., "2:SHA1:abXYZ123...:1234567890")
	/// </summary>
	private static bool IsPennMUSHPasswordFormat(string hash)
		=> !string.IsNullOrEmpty(hash) && TryParsePennMUSHHash(hash, out _, out _);

	/// <summary>
	/// Splits a PennMUSH-format hash (<c>V:ALGO:SALTEDHASH:TIMESTAMP</c>) into the algorithm and the
	/// salted hash without allocating the parts. Fails on a version other than 1 or 2, an unknown
	/// algorithm, or fewer than three fields; a trailing timestamp is optional and ignored.
	/// </summary>
	private static bool TryParsePennMUSHHash(string hash, out ReadOnlySpan<char> algo, out ReadOnlySpan<char> saltedHash)
	{
		algo = default;
		saltedHash = default;

		var text = hash.AsSpan();
		Span<System.Range> fields = stackalloc System.Range[4];
		if (text.Split(fields, ':') < 3)
			return false;

		if (!int.TryParse(text[fields[0]], out var version) || version < 1 || version > 2)
			return false;

		var candidateAlgo = text[fields[1]];
		if (!IsSha1(candidateAlgo) && !IsSha256(candidateAlgo))
			return false;

		algo = candidateAlgo;
		saltedHash = text[fields[2]];
		return true;
	}

	private static bool IsSha1(ReadOnlySpan<char> algo)
		=> algo.Equals("SHA1", StringComparison.OrdinalIgnoreCase) || algo.Equals("SHA-1", StringComparison.OrdinalIgnoreCase);

	private static bool IsSha256(ReadOnlySpan<char> algo)
		=> algo.Equals("SHA256", StringComparison.OrdinalIgnoreCase) || algo.Equals("SHA-256", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Verifies a password against the parts of a PennMUSH-format hash.
	/// The first 2 characters of <paramref name="saltedHash"/> are the salt, prepended to the plaintext
	/// before hashing; the rest is the hex digest, in either case.
	/// </summary>
	private static bool VerifyPennMUSHPassword(string plaintext, ReadOnlySpan<char> algo, ReadOnlySpan<char> saltedHash)
	{
		// The salt is the first 2 characters of the stored hash
		if (saltedHash.Length < 3)
			return false;

		var salt = saltedHash[..2];
		var expectedHash = saltedHash[2..];

		var saltedPlaintext = string.Concat(salt, plaintext);
		var byteCount = Encoding.UTF8.GetByteCount(saltedPlaintext);
		var rented = ArrayPool<byte>.Shared.Rent(byteCount);
		try
		{
			var input = rented.AsSpan(0, Encoding.UTF8.GetBytes(saltedPlaintext, rented));
			Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
			int digestLength;

			if (IsSha1(algo))
			{
				digestLength = SHA1.HashData(input, digest);
			}
			else if (IsSha256(algo))
			{
				digestLength = SHA256.HashData(input, digest);
			}
			else
			{
				return false;
			}

			// Compare hashes (case-insensitive as hex can be upper or lower)
			return expectedHash.Equals(Convert.ToHexString(digest[..digestLength]), StringComparison.OrdinalIgnoreCase);
		}
		finally
		{
			// The pooled buffer held the salted plaintext.
			CryptographicOperations.ZeroMemory(rented.AsSpan(0, byteCount));
			ArrayPool<byte>.Shared.Return(rented);
		}
	}

	public async ValueTask SetPassword(SharpPlayer user, string hashedPassword)
	{
		await mediator.Send(new SetPlayerPasswordCommand(user, hashedPassword));
	}

	public bool NeedsRehash(string hash)
	{
		return IsPennMUSHPasswordFormat(hash);
	}

	public async ValueTask RehashPasswordAsync(SharpPlayer player, string plaintext)
	{
		var userKey = $"#{player.Object.Key}:{player.Object.CreationTime}";
		var newHash = HashPassword(userKey, plaintext);
		await mediator.Send(new SetPlayerPasswordCommand(player, newHash, Salt: null));
	}
}
