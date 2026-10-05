using Mediator;
using SharpMUSH.Library.Attributes;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Expires cached grants after a role write that went straight to the role store: one object's when
/// <paramref name="Number"/> names it, every object's otherwise (a role's permissions changed, or an
/// account's roles or overrides, which reach every character on it). The handler does nothing; the
/// cache invalidation behaviour does the work.
/// </summary>
public record InvalidateGrantsCommand(int? Number) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => Number is { } number ? [Definitions.CacheKeys.Object(number)] : [];
	public string[] CacheTags => Number is null ? [Definitions.CacheTags.Grants] : [];
}
