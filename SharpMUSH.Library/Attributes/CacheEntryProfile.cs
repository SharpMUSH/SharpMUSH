namespace SharpMUSH.Library.Attributes;

/// <summary>
/// How a cached query's entries should live in FusionCache. The profile decides the one setting that
/// cannot be chosen per value: whether a stale entry may ever be served.
/// </summary>
/// <remarks>
/// FusionCache's <c>RemoveByTag</c> is an expire, not a delete: the entry stays behind as a fail-safe
/// fallback, by design ("removing entries by tag does not interfere with the fail-safe mechanism").
/// A key removal deletes the entry outright. So an entry invalidated by tag must not have fail-safe,
/// or a slow database after a move would hand back the room's pre-move contents - the exact loss
/// the per-container contents tag (#854) exists to prevent. The default derivation encodes that:
/// no tags means key-only invalidation and fail-safe is safe; any tag means it is not.
/// </remarks>
public enum CacheEntryProfile
{
	/// <summary>
	/// Invalidated only by key removal. Long-lived, fail-safe on: during a database outage the last
	/// known object is better than an error on every command, and no write can leave it behind
	/// because every write removes the key.
	/// </summary>
	Object,

	/// <summary>
	/// Invalidated by tag (alone or as well as by key). Long-lived like <see cref="Object"/>, but
	/// never served stale and never refreshed in the background: only a foreground factory's entry
	/// is stamped early enough for a tag removed mid-read to expire it.
	/// </summary>
	Tagged,

	/// <summary>
	/// A listing that can be large and is read rarely - zone members, log pages. Short-lived, low
	/// priority so it is the first thing evicted under memory pressure, never served stale, and
	/// weighted by its element count against the memory cache's size limit.
	/// </summary>
	Scan,
}
