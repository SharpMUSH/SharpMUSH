using Mediator;
using Microsoft.AspNetCore.Identity;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Services.Passwords;
using System.Security.Cryptography;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Hashes passwords with ASP.NET Core Identity's PBKDF2, and still verifies every stored-password
/// shape an imported PennMUSH database can hold (see <see cref="PennMUSHPasswords"/>). A legacy
/// password that verifies is rehashed onto PBKDF2 by the caller (<see cref="NeedsRehash"/>).
/// </summary>
public class PasswordService(IMediator mediator, PasswordHasher<string> hasher) : IPasswordService
{
	/// <summary>
	/// The stored password of a character that cannot log in until a wizard sets one, such as a player
	/// imported without a password. Nothing matches a stored value beginning with '!', the Unix shadow
	/// file's mark for a locked account and a character no hash above ever produces.
	/// </summary>
	public const string LockedHash = "!locked";

	private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

	public string GenerateRandomPassword()
		=> RandomNumberGenerator.GetString(Chars, 32);

	public string HashPassword(string user, string pw) =>
		hasher.HashPassword(user, pw);

	public bool PasswordIsValid(string user, string pw, string hash)
	{
		if (string.IsNullOrEmpty(hash) || hash.StartsWith('!'))
		{
			return false;
		}

		if (!IsIdentityHash(hash))
		{
			return PennMUSHPasswords.Verify(hash, pw);
		}

		try
		{
			return hasher.VerifyHashedPassword(user, hash, pw) != PasswordVerificationResult.Failed;
		}
		catch (FormatException)
		{
			return false;
		}
	}

	public async ValueTask SetPassword(SharpPlayer user, string hashedPassword)
	{
		await mediator.Send(new SetPlayerPasswordCommand(user, hashedPassword));
	}

	/// <summary>Anything that verified and is not already a PBKDF2 hash came from an imported database.</summary>
	public bool NeedsRehash(string hash)
		=> !string.IsNullOrEmpty(hash) && !hash.StartsWith('!') && !IsIdentityHash(hash);

	public async ValueTask RehashPasswordAsync(SharpPlayer player, string plaintext)
	{
		var userKey = $"#{player.Object.Key}:{player.Object.CreationTime}";
		var newHash = HashPassword(userKey, plaintext);
		await mediator.Send(new SetPlayerPasswordCommand(player, newHash, Salt: null));
	}

	/// <summary>
	/// Whether <paramref name="hash"/> is what <see cref="PasswordHasher{TUser}"/> writes: base64 of a
	/// format marker, 0x00 (version 2: marker, 16-byte salt, 32-byte subkey) or 0x01 (version 3: marker
	/// and a 12-byte header before salt and subkey). No PennMUSH or TinyMUX format decodes to either.
	/// </summary>
	private static bool IsIdentityHash(string hash)
	{
		if (hash.Length > 256)
		{
			return false;
		}

		Span<byte> decoded = stackalloc byte[hash.Length];
		if (!Convert.TryFromBase64String(hash, decoded, out var length) || length == 0)
		{
			return false;
		}

		return decoded[0] switch
		{
			0x00 => length == 49,
			0x01 => length >= 13,
			_ => false
		};
	}
}
