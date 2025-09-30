using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Library.Commands.Database;

public record SetObjectLocationCommand(AnySharpContent Target, AnySharpContainer Container): ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [$"object:{Target.Object().DBRef}", $"object:{Container.Object().DBRef}"];
	public string[] CacheTags => [];
}