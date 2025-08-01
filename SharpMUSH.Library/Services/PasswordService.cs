using Microsoft.AspNetCore.Identity;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class PasswordService(PasswordHasher<string> hasher) : IPasswordService
{
	public string HashPassword(string user, string pw) =>
		hasher.HashPassword(user, pw);

	public bool PasswordIsValid(string user, string pw, string hash) =>
		hasher.VerifyHashedPassword(user, hash, pw) != PasswordVerificationResult.Failed;
}