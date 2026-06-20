using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Library.Commands.Database;

public record SetObjectZoneCommand(AnySharpObject Target, AnySharpObject Zone) : ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Target.Object().DBRef), Definitions.CacheKeys.Object(Zone.Object().DBRef)];
	public string[] CacheTags => [];
}
