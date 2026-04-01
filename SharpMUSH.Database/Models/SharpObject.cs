using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Database.Models;

public record SharpObjectQueryResult(string Id, string Key, string Name, string[] Aliases, string Type, Dictionary<string, SharpLockDataQueryResult>? Locks, long CreationTime, long ModifiedTime, string PasswordHash, string? PasswordSalt, int Quota, WarningType Warnings = WarningType.None);

public record SharpObjectCreateRequest(string Name, string Type, Dictionary<string, SharpLockDataQueryResult> Locks, long CreationTime, long ModifiedTime);
