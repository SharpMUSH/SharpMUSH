namespace SharpMUSH.Database.Models;

/// <summary>
/// A power document as it comes back from the store. <c>Alias</c> and <c>Symbol</c> are nullable
/// because the seeded system powers omit both properties, and an absent JSON property deserializes
/// to null rather than to the empty string the model wants.
/// </summary>
public record SharpPowerQueryResult(string Id, string Key, bool System, bool Disabled, string Name, string? Alias, string? Symbol, string[] SetPermissions, string[] UnsetPermissions, string[] TypeRestrictions);

public record SharpPowerCreateRequest(string Name, string Alias, string Symbol, bool System, bool Disabled, string[] SetPermissions, string[] UnsetPermissions, string[] TypeRestrictions);
