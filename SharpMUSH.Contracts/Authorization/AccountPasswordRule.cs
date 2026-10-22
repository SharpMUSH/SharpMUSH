namespace SharpMUSH.Library.Authorization;

/// <summary>
/// The one rule an account password meets, wherever it is set: registration on the portal or at the connect
/// screen, a change, a staff reset and the setup claim. The portal checks it before sending and the server
/// enforces it in <c>IAccountService</c>, so no surface accepts a password another one would refuse.
/// Character passwords are PennMUSH's and keep PennMUSH's rules.
/// </summary>
public static class AccountPasswordRule
{
	public const int MinLength = 8;

	/// <summary>An upper bound on what is hashed, not a rule anyone is expected to meet.</summary>
	public const int MaxLength = 1024;

	public const string Requirement = "Password must be at least 8 characters.";

	public static bool Accepts(string? password)
		=> !string.IsNullOrWhiteSpace(password) && password.Length is >= MinLength and <= MaxLength;
}
