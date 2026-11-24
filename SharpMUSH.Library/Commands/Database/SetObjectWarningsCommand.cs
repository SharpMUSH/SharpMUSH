using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Commands.Database;

public record SetObjectWarningsCommand(AnySharpObject Target, WarningType Warnings) : ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Target.Object().DBRef)];
	public string[] CacheTags => [];
}
