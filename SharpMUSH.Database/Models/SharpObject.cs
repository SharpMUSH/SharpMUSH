namespace SharpMUSH.Database.Models;

public record SharpObjectQueryResult(string Id, string Key, string Name, string[] Aliases, string Type, Dictionary<string, SharpLockDataQueryResult>? Locks, long CreationTime, long ModifiedTime, string PasswordHash, string? PasswordSalt, int Quota);

public record SharpObjectCreateRequest(string Name, string Type, Dictionary<string, SharpLockDataQueryResult> Locks, long CreationTime, long ModifiedTime);
