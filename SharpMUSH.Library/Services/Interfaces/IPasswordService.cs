using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

public interface IPasswordService
{
	/// <summary>
	/// Creates a hashed version of the password provided for the user.
	/// </summary>
	/// <param name="user">#DBREF:CREATEDMILLISECONDS</param>
	/// <param name="pw">The (new) Password</param>
	/// <returns>The Hashed Password</returns>
	string HashPassword(string user, string pw);

	/// <summary>
	/// Verifies whether or not the password is valid against the hash.
	/// </summary>
	/// <param name="user">#DBREF:CREATEDMILLISECONDS</param>
	/// <param name="pw">Attempted Password</param>
	/// <param name="hash">Stored Password Hash</param>
	/// <returns>Success or Failure</returns>
	bool PasswordIsValid(string user, string pw, string hash);

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
}