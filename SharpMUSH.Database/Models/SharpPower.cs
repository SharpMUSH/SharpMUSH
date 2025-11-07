namespace SharpMUSH.Database.Models;

public record SharpPowerQueryResult(string Id, string Key, bool System, bool Disabled, string Name, string Alias, string[] SetPermissions, string[] UnsetPermissions, string[] TypeRestrictions);

public record SharpPowerCreateRequest(string Name, string Alias, bool System, bool Disabled, string[] SetPermissions, string[] UnsetPermissions, string[] TypeRestrictions);
