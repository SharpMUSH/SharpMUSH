namespace SharpMUSH.Database.Models;

public record SharpObjectFlagQueryResult(string Id, string Key, string Name, string[]? Aliases, string Symbol, bool System, string[] SetPermissions, string[] UnsetPermissions, string[] TypeRestrictions);

public record SharpObjectFlagCreationRequest(string Name, string[]? Aliases, string Symbol, bool System, string[] SetPermissions, string[] UnsetPermissions, string[] TypeRestrictions);