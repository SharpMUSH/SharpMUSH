using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Commands.Database;

public record UnsetObjectZoneCommand(AnySharpObject Target) : ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Target.Object().DBRef)];
	public string[] CacheTags => [Definitions.CacheTags.ZoneObjects];
}
