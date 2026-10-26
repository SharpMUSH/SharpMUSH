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
/// Tagged with the object's <see cref="CacheKeys.GrantsTag"/>, which the commands writing a role, an
/// override, a flag or a power on the object remove, and with <see cref="CacheTags.Grants"/>, which a
/// change no single object names (a role's permissions, an account's roles or overrides, a character
/// linked or unlinked, an account's status) clears for every object.
/// </remarks>
public record GetObjectGrantsQuery(int Number, bool IsPlayer) : IQuery<ObjectGrants>, ICacheable
{
	// Built once per query: every privilege check sends one, and the caching behaviour reads both more
	// than once, so a computed property allocated a key, a tag array and a tag string on each read.
	public string CacheKey { get; } = $"grants:#{Number}:{(IsPlayer ? 'p' : 'o')}";
	public string[] CacheTags { get; } = [CacheKeys.GrantsTag(Number), Definitions.CacheTags.Grants];
}
