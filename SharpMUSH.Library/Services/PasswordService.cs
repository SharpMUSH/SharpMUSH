using System.Security.Cryptography;
using System.Text;
using Mediator;
using Microsoft.AspNetCore.Identity;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

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

		// Check if this is a PennMUSH-format password (V:ALGO:HASH:TIMESTAMP)
		if (IsPennMUSHPasswordFormat(hash))
		{
			return VerifyPennMUSHPassword(pw, hash);
		}

		// Use modern PBKDF2 verification
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
	{
		if (string.IsNullOrEmpty(hash))
			return false;

		var parts = hash.Split(':');
		if (parts.Length < 3)
			return false;

		// Check if first part is a version number (1 or 2)
		if (!int.TryParse(parts[0], out var version) || version < 1 || version > 2)
			return false;

		// Check if second part is a known algorithm
		var algo = parts[1].ToUpperInvariant();
		return algo is "SHA1" or "SHA-1" or "SHA256" or "SHA-256";
	}

	/// <summary>
	/// Verifies a password against a PennMUSH-format hash.
	/// The hash format is: V:ALGO:SALTEDHASH:TIMESTAMP
	/// The first 2 characters of SALTEDHASH are the salt, prepended to the plaintext before hashing.
	/// </summary>
	private static bool VerifyPennMUSHPassword(string plaintext, string storedHash)
	{
		var parts = storedHash.Split(':');
		if (parts.Length < 3)
			return false;

		var algo = parts[1].ToUpperInvariant();
		var saltedHash = parts[2];

		// The salt is the first 2 characters of the stored hash
		if (saltedHash.Length < 3)
			return false;

		var salt = saltedHash[..2];
		var expectedHash = saltedHash[2..];

		// Compute the hash: salt + plaintext
		var saltedPlaintext = salt + plaintext;
		string computedHash;

		if (algo is "SHA1" or "SHA-1")
		{
			computedHash = ComputeSha1Hash(saltedPlaintext);
		}
		else if (algo is "SHA256" or "SHA-256")
		{
			computedHash = ComputeSha256Hash(saltedPlaintext);
		}
		else
		{
			// Unknown algorithm
			return false;
		}

		// Compare hashes (case-insensitive as hex can be upper or lower)
		return string.Equals(computedHash, expectedHash, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Computes SHA1 hash of the input string and returns it as a hex string.
	/// </summary>
	private static string ComputeSha1Hash(string input)
	{
		var bytes = Encoding.UTF8.GetBytes(input);
		var hashBytes = SHA1.HashData(bytes);
		return Convert.ToHexString(hashBytes).ToLowerInvariant();
	}

	/// <summary>
	/// Computes SHA256 hash of the input string and returns it as a hex string.
	/// </summary>
	private static string ComputeSha256Hash(string input)
	{
		var bytes = Encoding.UTF8.GetBytes(input);
		var hashBytes = SHA256.HashData(bytes);
		return Convert.ToHexString(hashBytes).ToLowerInvariant();
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
