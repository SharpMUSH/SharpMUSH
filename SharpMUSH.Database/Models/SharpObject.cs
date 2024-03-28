using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.Models;

public record SharpObjectQueryResult(string Id, string Key, string Name, string Type, Dictionary<string, SharpObject.SharpLock> Locks, long CreationTime, long ModifiedTime);

public record SharpObjectCreateRequest(string Name, string Type, Dictionary<string, SharpObject.SharpLock> Locks, long CreationTime, long ModifiedTime);