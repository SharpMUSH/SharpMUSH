using System.Security.Cryptography;
using System.Text;
using Mediator;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class PasswordServiceTests: TestClassFactory
{
	private IPasswordService PasswordService =>
		Factory.Services.GetRequiredService<IPasswordService>();

	[Test]
	public async ValueTask ModernPassword_ValidPassword_ReturnsTrue()
	{
		var user = "#1:12345";
		var password = "TestPassword123";
		var hash = PasswordService.HashPassword(user, password);

		var result = PasswordService.PasswordIsValid(user, password, hash);

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async ValueTask ModernPassword_InvalidPassword_ReturnsFalse()
	{
		var user = "#1:12345";
		var password = "TestPassword123";
		var wrongPassword = "WrongPassword456";
		var hash = PasswordService.HashPassword(user, password);

		var result = PasswordService.PasswordIsValid(user, wrongPassword, hash);

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async ValueTask PennMUSHPassword_SHA1_ValidPassword_ReturnsTrue()
	{
		var password = "mypassword";
		var salt = "ab";
		var pennMUSHHash = CreatePennMUSHHash(salt, password, "SHA1");

		var result = PasswordService.PasswordIsValid("ignored", password, pennMUSHHash);

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async ValueTask PennMUSHPassword_SHA1_InvalidPassword_ReturnsFalse()
	{
		var password = "mypassword";
		var wrongPassword = "wrongpassword";
		var salt = "ab";
		var pennMUSHHash = CreatePennMUSHHash(salt, password, "SHA1");

		var result = PasswordService.PasswordIsValid("ignored", wrongPassword, pennMUSHHash);

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async ValueTask PennMUSHPassword_SHA1_DifferentSalts_BothWork()
	{
		var password = "samepassword";
		var salt1 = "Xy";
		var salt2 = "Z9";
		var hash1 = CreatePennMUSHHash(salt1, password, "SHA1");
		var hash2 = CreatePennMUSHHash(salt2, password, "SHA1");

		await Assert.That(hash1).IsNotEqualTo(hash2);
		await Assert.That(PasswordService.PasswordIsValid("ignored", password, hash1)).IsTrue();
		await Assert.That(PasswordService.PasswordIsValid("ignored", password, hash2)).IsTrue();
	}

	[Test]
	public async ValueTask PennMUSHPassword_SHA256_ValidPassword_ReturnsTrue()
	{
		var password = "mypassword";
		var salt = "cd";
		var pennMUSHHash = CreatePennMUSHHash(salt, password, "SHA256");

		var result = PasswordService.PasswordIsValid("ignored", password, pennMUSHHash);

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async ValueTask PennMUSHPassword_SHA256_InvalidPassword_ReturnsFalse()
	{
		var password = "mypassword";
		var wrongPassword = "wrongpassword";
		var salt = "cd";
		var pennMUSHHash = CreatePennMUSHHash(salt, password, "SHA256");

		var result = PasswordService.PasswordIsValid("ignored", wrongPassword, pennMUSHHash);

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async ValueTask PennMUSHPassword_Version1_ValidPassword_ReturnsTrue()
	{
		var password = "mypassword";
		var salt = "ef";
		var pennMUSHHash = CreatePennMUSHHash(salt, password, "SHA1", version: 1);

		var result = PasswordService.PasswordIsValid("ignored", password, pennMUSHHash);

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async ValueTask PennMUSHPassword_WithTimestamp_ValidPassword_ReturnsTrue()
	{
		var password = "mypassword";
		var salt = "gh";
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		var pennMUSHHash = CreatePennMUSHHash(salt, password, "SHA1", timestamp: timestamp);

		var result = PasswordService.PasswordIsValid("ignored", password, pennMUSHHash);

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async ValueTask EmptyHash_ReturnsFalse()
	{
		var result = PasswordService.PasswordIsValid("user", "password", "");

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async ValueTask NullHash_ReturnsFalse()
	{
		var result = PasswordService.PasswordIsValid("user", "password", null!);

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async ValueTask InvalidPennMUSHFormat_TooFewParts_FallsBackToModern()
	{
		var result = PasswordService.PasswordIsValid("user", "password", "invalid:hash");

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async ValueTask InvalidPennMUSHFormat_InvalidVersion_FallsBackToModern()
	{
		var result = PasswordService.PasswordIsValid("user", "password", "99:SHA1:hash:12345");

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async ValueTask InvalidPennMUSHFormat_UnknownAlgorithm_ReturnsFalse()
	{
		var result = PasswordService.PasswordIsValid("user", "password", "2:MD5:hash:12345");

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async ValueTask GenerateRandomPassword_Returns32Characters()
	{
		var password = PasswordService.GenerateRandomPassword();

		await Assert.That(password.Length).IsEqualTo(32);
	}

	[Test]
	public async ValueTask GenerateRandomPassword_ContainsOnlyValidCharacters()
	{
		var validChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
		var password = PasswordService.GenerateRandomPassword();

		foreach (var c in password)
		{
			await Assert.That(validChars.Contains(c)).IsTrue();
		}
	}

	[Test]
	public async ValueTask PennMUSHPassword_CaseInsensitiveHashComparison_ReturnsTrue()
	{
		var password = "mypassword";
		var salt = "ij";
		var saltedPlaintext = salt + password;
		var hashBytes = SHA1.HashData(Encoding.UTF8.GetBytes(saltedPlaintext));
		var upperHash = Convert.ToHexString(hashBytes).ToUpperInvariant();
		var pennMUSHHash = $"2:SHA1:{salt}{upperHash}:12345";

		var result = PasswordService.PasswordIsValid("ignored", password, pennMUSHHash);

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async ValueTask NeedsRehash_PennMUSHFormat_ReturnsTrue()
	{
		var pennMUSHHash = CreatePennMUSHHash("ab", "password", "SHA1");

		var result = PasswordService.NeedsRehash(pennMUSHHash);

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async ValueTask NeedsRehash_ModernPBKDF2Format_ReturnsFalse()
	{
		var modernHash = PasswordService.HashPassword("#1:12345", "password");

		var result = PasswordService.NeedsRehash(modernHash);

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async ValueTask NeedsRehash_EmptyHash_ReturnsFalse()
	{
		var result = PasswordService.NeedsRehash("");

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async ValueTask NeedsRehash_NullHash_ReturnsFalse()
	{
		var result = PasswordService.NeedsRehash(null!);

		await Assert.That(result).IsFalse();
	}

	private static string CreatePennMUSHHash(string salt, string password, string algorithm, int version = 2, long? timestamp = null)
	{
		var saltedPlaintext = salt + password;
		byte[] hashBytes;

		if (algorithm.Equals("SHA1", StringComparison.OrdinalIgnoreCase))
		{
			hashBytes = SHA1.HashData(Encoding.UTF8.GetBytes(saltedPlaintext));
		}
		else if (algorithm.Equals("SHA256", StringComparison.OrdinalIgnoreCase))
		{
			hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(saltedPlaintext));
		}
		else
		{
			throw new ArgumentException($"Unknown algorithm: {algorithm}");
		}

		var hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();
		var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

		return $"{version}:{algorithm}:{salt}{hashHex}:{ts}";
	}
}
