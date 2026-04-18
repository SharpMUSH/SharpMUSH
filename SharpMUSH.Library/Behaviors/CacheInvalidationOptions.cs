namespace SharpMUSH.Library.Behaviors;

/// <summary>
/// Options that control cache-invalidation behavior.
/// </summary>
public class CacheInvalidationOptions
{
	/// <summary>
	/// When <c>true</c>, the cache is invalidated a second time <em>after</em> the
	/// command handler completes.  This guards against concurrent reads that
	/// repopulate the cache with stale data while the write is in progress.
	/// <para>
	/// Enable in test environments that run many write+read operations in
	/// parallel.  In production a single pre-handler invalidation is usually
	/// sufficient and avoids the extra round-trip.
	/// </para>
	/// </summary>
	public bool InvalidateAfterHandler { get; set; }
}
