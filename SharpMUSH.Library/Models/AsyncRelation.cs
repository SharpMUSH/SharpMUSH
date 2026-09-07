namespace SharpMUSH.Library.Models;

/// <summary>
/// A relation of a loaded object that is resolved on every read rather than memoised on the
/// instance. Same <see cref="WithCancellation"/> shape as <c>AsyncLazy&lt;T&gt;</c>, so call sites
/// are unchanged.
/// </summary>
/// <remarks>
/// A loaded object lives in the cache for minutes and is shared by every reader. A relation
/// memoised on it - the room an object was in the first time anyone asked - would outlive every
/// invalidation of that room: the cache entry for the answer is expired correctly, and the
/// memo keeps handing back the old instance regardless. Resolving through the loader on every
/// read costs a cache hit and follows invalidation.
/// </remarks>
public sealed class AsyncRelation<T>(Func<CancellationToken, Task<T>> resolve)
{
	public Task<T> WithCancellation(CancellationToken token) => resolve(token);
}
