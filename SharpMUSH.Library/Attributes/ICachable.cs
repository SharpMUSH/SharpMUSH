namespace SharpMUSH.Library.Attributes;

/// <summary>
/// Removed by <see cref="SharpMUSH.Library.Attributes.ICacheInvalidating"/>
/// Handled by <see cref="SharpMUSH.Library.Behaviors.QueryCachingBehavior{TRequest, TResponse}"/>
/// and <see cref="SharpMUSH.Library.Behaviors.StreamQueryCachingBehavior{TRequest, TResponse}"/>
/// </summary>
public interface ICacheable
{
	string CacheKey { get; }
	string[] CacheTags { get; }

	/// <summary>
	/// The entry profile the caching behaviours apply. Derived from the tags unless a query says
	/// otherwise: tagged entries must never be served stale (see <see cref="CacheEntryProfile"/>).
	/// Override only to opt a large or rarely read listing into <see cref="CacheEntryProfile.Scan"/>.
	/// </summary>
	CacheEntryProfile Profile => CacheTags.Length == 0 ? CacheEntryProfile.Object : CacheEntryProfile.Tagged;
}