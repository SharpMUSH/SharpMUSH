using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Library.Commands.Database;

public record SetObjectHomeCommand(AnySharpContent Target, AnySharpContainer Home) : ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [$"object:{Target.Object().DBRef}", $"object:{Home.Object().DBRef}"];
	public string[] CacheTags => [];
}