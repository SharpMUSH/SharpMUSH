namespace SharpMUSH.Library.Models;

/// <summary>A native login command's secret is returned once, solely by the authenticated mint response.</summary>
public sealed record NativeLoginSecretResponse(string Id, string Secret, int CharacterKey, long CharacterCreationTime, DateTimeOffset ExpiresAt);
public sealed record NativeLoginStatusResponse(string Id, int CharacterKey, long CharacterCreationTime, DateTimeOffset ExpiresAt);
