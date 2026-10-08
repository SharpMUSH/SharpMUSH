using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Library.Commands.Database;

public record UnsetObjectParentCommand(AnySharpObject Target) : ICommand, ICacheInvalidating
{
	// The parent is not modified, but every read through the target's chain, its children's included, now walks a shorter one.
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Target.Object().DBRef)];
	public string[] CacheTags => [Definitions.CacheTags.InheritedAttributes];
}