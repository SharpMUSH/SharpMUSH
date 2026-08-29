using SharpMUSH.Library.Attributes;

namespace SharpMUSH.Library.Behaviors;

/// <summary>
/// The tag set an entry is stored under: everything the query declares, plus its own cache key.
/// </summary>
/// <remarks>
/// Tagging an entry with its own key is what makes a key-targeted invalidation survive a read that
/// straddles it. <c>IFusionCache.RemoveAsync</c> only drops what is in the cache at that instant, so a
/// read whose database query was issued before a write and whose factory returns after the write's
/// invalidation stores its pre-write answer on top — and every later reader is served that stale entry
/// until something else happens to invalidate the key. <c>RemoveByTagAsync</c> instead records the
/// invalidation, and FusionCache resolves it against when the entry's factory <em>began</em> rather
/// than when it stored, so the late store is recognised as pre-write and is not served.
/// <c>CachingBehaviorTests.StraddlingRead_DoesNotOutliveTheWriteThatInvalidatedIt</c> pins that
/// difference between the two: it fails on the key-targeted case alone.
/// <para>
/// That is issue #838: <c>CreatePlayerCommand</c> invalidates <c>object-contents:#N</c> by key alone,
/// so a player created inside another reader's contents read vanished from the room's cached contents
/// and <c>FOLLOW</c> reported "I can't see that here." for someone standing right there. It is the
/// window <see cref="CacheInvalidationBehavior{TRequest,TResponse}"/> used to document as open, and
/// the same shape as issue #797.
/// </para>
/// </remarks>
internal static class CacheEntryTags
{
	public static string[] For(ICacheable message)
		=> [message.CacheKey, .. message.CacheTags];
}
