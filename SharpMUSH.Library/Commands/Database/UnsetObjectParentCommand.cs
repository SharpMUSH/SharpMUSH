using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Library.Commands.Database;

public record UnsetObjectParentCommand(AnySharpObject Target) : ICommand, ICacheInvalidating
{
	// Invalidate cache for the target object only
	// The parent (if it exists) doesn't need invalidation since we're only modifying the child
	public string[] CacheKeys => [$"object:{Target.Object().DBRef}"];
	public string[] CacheTags => [];
}