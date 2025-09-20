using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SetObjectOwnerCommand(AnySharpObject Target, SharpPlayer Owner): ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [$"object:{Target.Object().DBRef}", $"object:{Owner.Object.DBRef}"];
	public string[] CacheTags => [];
}