using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreateThingCommand(string Name, AnySharpContainer Where, SharpPlayer Owner) : ICommand<DBRef>, ICacheInvalidating
{
	public string[] CacheKeys => [Where.Object().DBRef.ToString()];
	
	public string[] CacheTags => [
		Definitions.CacheTags.ObjectList, 
		Definitions.CacheTags.ThingList, 
		Definitions.CacheTags.ObjectContents, 
		Definitions.CacheTags.ObjectOwnership
	];
}