using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Reads visible inherited patterns from an ancestor and its bounded parent chain, without consulting
/// further configured ancestors. The complete per-object snapshots are cached; the aggregate is not,
/// so parent-link changes are observed on every search.
/// </summary>
/// <remarks>Configured type-ancestor listen fallback is a SharpMUSH extension.</remarks>
public record GetAncestorListenAttributesQuery(DBRef Ancestor) : IQuery<ListenAttributeCache[]>
{
	// Compatibility metadata; this query intentionally does not implement ICacheable.
	public string CacheKey => Definitions.CacheKeys.AncestorListens(Ancestor.Number);
	public string[] CacheTags => [];
}
