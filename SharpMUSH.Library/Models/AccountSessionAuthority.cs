namespace SharpMUSH.Library.Models;

/// <summary>A live-session projection without its bearer token, addressed only by its SHA-256 digest.</summary>
public sealed record AccountSessionAuthority(string AccountId, long ExpiryUnixMs, int? CharacterKey,
	long? CharacterCreationTime, bool Remembered);
