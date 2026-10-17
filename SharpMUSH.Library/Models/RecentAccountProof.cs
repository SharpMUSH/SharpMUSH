namespace SharpMUSH.Library.Models;

/// <summary>Which explicit account method was proved; never contains a credential.</summary>
public sealed record AccountProofAuthority(string Method, string? IdentityId = null, long CredentialEpoch = 0, string? ProviderGeneration = null);
