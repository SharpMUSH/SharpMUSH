using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// What object <paramref name="Number"/> is granted, read behind <c>SharpObject.Grants</c> through
/// <c>IObjectRelationLoader</c>. Every privilege check asks it, so it is cached.
/// </summary>
/// <remarks>
/// Tagged with the object's own <see cref="CacheKeys.ObjectTag"/>, so a role or override written on the
/// object (which removes <c>object:#N</c>) expires it, and with <see cref="CacheTags.Grants"/>, which a
/// change no single object names (a role's permissions, an account's roles or overrides, a character
/// linked or unlinked, an account's status) clears for every object.
/// </remarks>
public record GetObjectGrantsQuery(int Number, bool IsPlayer) : IQuery<ObjectGrants>, ICacheable
{
	public string CacheKey => $"grants:#{Number}:{(IsPlayer ? 'p' : 'o')}";
	public string[] CacheTags => [CacheKeys.ObjectTag(Number), Definitions.CacheTags.Grants];
}
