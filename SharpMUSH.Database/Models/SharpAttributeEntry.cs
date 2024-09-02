namespace SharpMUSH.Database.Models;

public record SharpAttributeEntryQueryResult(string Id, string Key, string Name, string[] DefaultFlags, string? Limit, string[]? Enum);

public record SharpAttributeEntryCreateRequest(string Key, string Name, string[] DefaultFlags, string? Limit, string[]? Enum);