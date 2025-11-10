namespace SharpMUSH.Database.Models;

public record SharpPlayerQueryResult(string Id, string[]? Aliases, string PasswordHash, int Quota);

public record SharpPlayerCreateRequest(string[]? Aliases, string PasswordHash, int Quota);