using Mediator;
using Microsoft.AspNetCore.Identity;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class PasswordService(IMediator mediator, PasswordHasher<string> hasher) : IPasswordService
{
	private static readonly Random random = new();
	private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

	public string GenerateRandomPassword() 
		=> new([.. Enumerable.Repeat(Chars, 32).Select(s => s[random.Next(s.Length)])]);

	public string HashPassword(string user, string pw) =>
		hasher.HashPassword(user, pw);

	public bool PasswordIsValid(string user, string pw, string hash) =>
		hasher.VerifyHashedPassword(user, hash, pw) != PasswordVerificationResult.Failed;

	public async ValueTask SetPassword(SharpPlayer user, string hashedPassword)
	{
		await mediator.Send(new SetPlayerPasswordCommand(user, hashedPassword));
	}
}