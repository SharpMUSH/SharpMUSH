using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models;

public sealed record ResolvedLock(string Name, AnySharpObject Source, SharpLockData Data);
