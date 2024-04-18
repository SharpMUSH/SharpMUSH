namespace SharpMUSH.Database.Models;

public record SharpObjectQueryResult(string Id, string Key, string Name, string Type, Dictionary<string, string> Locks, long CreationTime, long ModifiedTime);

public record SharpObjectCreateRequest(string Name, string Type, Dictionary<string, string> Locks, long CreationTime, long ModifiedTime);