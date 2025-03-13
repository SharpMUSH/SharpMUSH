using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record MoveObjectCommand(AnySharpContent Target, AnySharpContainer Destination)
	: ICommand<DBRef>, ICacheInvalidating
{
	public string[] CacheKeys => [Target.Object().DBRef.ToString(), Destination.Object().DBRef.ToString()];

	public string[] CacheTags => [Definitions.CacheTags.ObjectContents];
}