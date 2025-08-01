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
}