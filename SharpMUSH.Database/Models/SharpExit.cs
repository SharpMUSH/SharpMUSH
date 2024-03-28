namespace SharpMUSH.Database.Models;

public record SharpExitQueryResult(string Id, string Key, string[]? Aliases);

public record SharpExitCreateRequest(string[]? Aliases);