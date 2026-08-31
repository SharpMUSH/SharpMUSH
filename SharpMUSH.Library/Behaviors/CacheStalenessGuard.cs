using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Library.Behaviors;

/// <summary>
/// Refuses to store a read that a write overtook while it was in flight.
/// </summary>
/// <remarks>
/// One copy for both caching behaviours: they ask the same three things — stamp before the read, ask
/// <see cref="ICacheInvalidationClock.InvalidatedSince"/> after it, disable the write — and two copies
/// drift. They already did, the first time the guard had to skip the distributed write as well as the
/// memory one.
/// </remarks>
internal static class CacheStalenessGuard
{
	/// <summary>
	/// Call after the read returns, with the stamp taken before it started. The caller keeps the value
	/// either way: it is what the database said, and only keeping it for the next reader is wrong.
	/// </summary>
	public static void SkipWriteIfInvalidated<TValue>(
		FusionCacheFactoryExecutionContext<TValue> ctx,
		ICacheInvalidationClock clock,
		string cacheKey,
		long readStartedAt)
	{
		if (!clock.InvalidatedSince(cacheKey, readStartedAt)) return;

		// Both layers: memory is the only one configured today, but a distributed cache added later would
		// otherwise be handed exactly the answer we are refusing to keep. Nothing was written, so there is
		// nothing for the backplane to announce either.
		ctx.Options
			.SetSkipMemoryCacheWrite(true)
			.SetSkipDistributedCacheWrite(true, skipBackplaneNotifications: true);
	}
}
