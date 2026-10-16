namespace SharpMUSH.Library.Models;

/// <summary>The complete, immutable remote identity namespace. Email is never an identity key.</summary>
public sealed record ExternalIdentityKey(string Registration, string Issuer, string Subject);

/// <summary>An account sign-in identity, independent of bot integrations and mutable remote profile fields.</summary>
public sealed record ExternalIdentity(string Id, string AccountId, ExternalIdentityKey Key, string? Label,
	DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);
