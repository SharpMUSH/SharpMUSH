using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// The relations of one object that live outside its document and point at another object:
/// owner, parent, zone. Read behind <c>SharpObject.Owner</c> / <c>.Parent</c> / <c>.Zone</c>
/// through <c>IObjectRelationLoader</c>, so they answer from cache and follow invalidation.
/// </summary>
/// <remarks>
/// Tagged with the subject's own <see cref="CacheKeys.ObjectTag"/>, so any write to the subject
/// (a new parent, a new owner, a new zone - all of which remove <c>object:#N</c>) expires the
/// answer; the caching behaviours also tag the entry with the object the answer embeds, so a
/// write to the parent itself expires it as well.
/// </remarks>
public record GetOwnerOfQuery(string Id, int Number) : IQuery<SharpPlayer>, ICacheable
{
	public string CacheKey => $"owner-of:{Id}";
	public string[] CacheTags => [CacheKeys.ObjectTag(Number)];
}

public record GetParentOfQuery(string Id, int Number) : IQuery<AnyOptionalSharpObject>, ICacheable
{
	public string CacheKey => $"parent-of:{Id}";
	public string[] CacheTags => [CacheKeys.ObjectTag(Number)];
}

public record GetZoneOfQuery(string Id, int Number) : IQuery<AnyOptionalSharpObject>, ICacheable
{
	public string CacheKey => $"zone-of:{Id}";
	public string[] CacheTags => [CacheKeys.ObjectTag(Number)];
}
