namespace SharpMUSH.Database.Models;

public record SharpCommandQueryResult(string Id, string Key, string Name, string? Alias, bool Enabled, string? RestrictedErrorMessage, string[] Traits, string[] Restrictions);

public record SharpCommandCreateRequest(string Name, string? Alias, bool Enabled, string? RestrictedErrorMessage, string[] Traits, string[] Restrictions);