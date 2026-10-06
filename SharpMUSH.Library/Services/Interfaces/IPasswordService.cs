using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

public interface IPasswordService
{
	/// <summary>
	/// Hashes a password. The hash carries its own random salt, so it does not depend on whose password
	/// it is, and can be computed before the object or account that will hold it exists.
	/// </summary>
	/// <param name="pw">The (new) Password</param>
	/// <returns>The Hashed Password</returns>
	string HashPassword(string pw);

	/// <summary>
	/// Verifies whether or not the password is valid against the hash.
	/// </summary>
	/// <param name="pw">Attempted Password</param>
	/// <param name="hash">Stored Password Hash</param>
	/// <returns>Success or Failure</returns>
	bool PasswordIsValid(string pw, string hash);

	/// <summary>
	/// Sets the password for the user, requiring it to be hashed first.
	/// </summary>
	/// <param name="user">SharpPlayer</param>
	/// <param name="hashedPassword">The hashed password.</param>
	ValueTask SetPassword(SharpPlayer user, string hashedPassword);

	/// <summary>
	/// Generates a random password.
	/// </summary>
	/// <returns>A random password</returns>
	string GenerateRandomPassword();

	/// <summary>
	/// Checks if a password hash needs to be rehashed (e.g., legacy PennMUSH format).
	/// </summary>
	/// <param name="hash">The stored password hash</param>
	/// <returns>True if the hash should be upgraded to modern format</returns>
	bool NeedsRehash(string hash);

	/// <summary>
	/// Rehashes a password using modern PBKDF2 and updates the database.
	/// Call this after successful verification of a legacy password to upgrade it.
	/// </summary>
	/// <param name="player">The player whose password to rehash</param>
	/// <param name="plaintext">The plaintext password (captured during login)</param>
	ValueTask RehashPasswordAsync(SharpPlayer player, string plaintext);
}
