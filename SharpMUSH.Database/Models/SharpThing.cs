namespace SharpMUSH.Database.Models;

public record SharpThingQueryResult(string Id, string Key, string[]? Aliases);

public record SharpThingCreateRequest(string[]? Aliases);