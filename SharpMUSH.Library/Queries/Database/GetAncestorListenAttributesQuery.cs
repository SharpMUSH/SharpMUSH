using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Query for the ^-listen contribution of a type ancestor (PennMUSH ANCESTOR_*): the ancestor's own
/// listen attributes plus those along the ancestor's OWN LISTEN_PARENT chain (no ancestor-of-ancestor),
/// with regex pre-compiled. The set depends only on the ancestor subtree, so it is cached keyed by
/// ancestor dbref (computed once per ancestor rather than re-walked for every listener that falls
/// through to it). Invalidated when the ancestor's attributes change via the shared
/// <c>ancestor-listens:{ancestor}</c> invalidation key emitted by attribute-set/clear/flag commands.
/// </summary>
public record GetAncestorListenAttributesQuery(DBRef Ancestor) : IQuery<ListenAttributeCache[]>, ICacheable
{
	// Keyed by dbref NUMBER only (see GetAncestorCommandAttributesQuery): must match the number-only
	// invalidation key carried by the attribute-mutating commands.
	public string CacheKey => $"ancestor-listens:#{Ancestor.Number}";
	public string[] CacheTags => [];
}
