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

	/// <summary>
	/// Whether the stored result also carries one object tag per object it names, so a write to any of
	/// them expires it (see <see cref="SharpMUSH.Library.Behaviors.EmbeddedObjectTags"/>). Every tag is
	/// checked on every hit, so a list pays per member on each read. Turn it off only for a list whose
	/// membership no write to a member can change except through one of the query's own tags.
	/// </summary>
	bool TagsEmbeddedObjects => true;
}