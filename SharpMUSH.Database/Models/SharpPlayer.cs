namespace SharpMUSH.Database.Models;

public record SharpPlayerQueryResult(string Id, string[]? Aliases, string PasswordHash);

public record SharpPlayerCreateRequest(string[]? Aliases, string PasswordHash);