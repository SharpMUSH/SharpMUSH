namespace SharpMUSH.Library.Models;

/// <summary>The authority retained behind a native secret; never contains any bearer credential.</summary>
public sealed record NativeLoginAuthority(string AccountId, DBRef Character, string SessionDigest,
	long AccountCredentialEpoch, long PasswordCredentialEpoch);
public sealed record NativeLoginSecretLease(NativeLoginAuthority Authority, DateTimeOffset ExpiresAt);
public sealed record NativeLoginSecretMint(string Id, string Secret, DBRef Character, DateTimeOffset ExpiresAt);
public sealed record NativeLoginSecretStatus(string Id, DBRef Character, DateTimeOffset ExpiresAt);

/// <summary>Fresh provider snapshot of exact character ownership and credential versions.</summary>
public sealed record NativeCharacterAuthority(string AccountId, DBRef Character, long AccountCredentialEpoch, long PasswordCredentialEpoch);
