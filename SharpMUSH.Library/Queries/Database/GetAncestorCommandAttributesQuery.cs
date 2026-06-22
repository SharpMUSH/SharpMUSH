using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Query for the command-attribute contribution of a type ancestor (PennMUSH ANCESTOR_*).
/// This resolves the ancestor's own $command attributes plus those inherited along the ancestor's
/// OWN @parent chain (no ancestor-of-ancestor), with regex pre-compiled — exactly the set the
/// per-object command scan would derive when falling through to the ancestor.
///
/// The result is cached keyed by ancestor dbref so it is computed ONCE per ancestor rather than
/// redundantly re-derived for every child object that falls through to it. Invalidated whenever the
/// ancestor's own attributes change (it shares the <c>commands:{ancestor}</c> invalidation key emitted
/// by attribute-set/clear/flag commands), so a <c>@set</c> on the ancestor stays visible.
/// </summary>
public record GetAncestorCommandAttributesQuery(DBRef Ancestor) : IQuery<CommandAttributeCache[]>, ICacheable
{
	// Keyed by dbref NUMBER only (not DBRef.ToString(), which appends CreationMilliseconds): the
	// invalidating attribute commands carry the number-only key, and the resolved ancestor ref may
	// arrive without millis, so both sides must agree on the number-only form.
	public string CacheKey => $"ancestor-commands:#{Ancestor.Number}";
	public string[] CacheTags => [];
}
