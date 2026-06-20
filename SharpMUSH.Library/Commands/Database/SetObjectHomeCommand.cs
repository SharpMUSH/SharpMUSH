using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Library.Commands.Database;

public record SetObjectHomeCommand(AnySharpContent Target, AnySharpContainer Home) : ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Target.Object().DBRef), Definitions.CacheKeys.Object(Home.Object().DBRef)];
	public string[] CacheTags => [];
}