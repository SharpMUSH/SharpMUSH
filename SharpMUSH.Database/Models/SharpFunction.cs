namespace SharpMUSH.Database.Models;

public record SharpFunctionQueryResult(string Id, string Key, string Name, string? Alias, bool Enabled, string? RestrictedErrorMessage, string[] Traits, int MinArgs, int MaxArgs, string[] Restrictions);
