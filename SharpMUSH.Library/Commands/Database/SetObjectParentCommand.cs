using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Library.Commands.Database;

public record SetObjectParentCommand(AnySharpObject Target, AnySharpObject Parent) : ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Target.Object().DBRef), Definitions.CacheKeys.Object(Parent.Object().DBRef)];
	public string[] CacheTags => [];
}