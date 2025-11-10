using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreatePlayerCommand(string Name, string Password, DBRef Location, DBRef Home, int Quota) : ICommand<DBRef>, ICacheInvalidating
{
	public string[] CacheKeys => [$"object-contents:{Location}"];
	
	public string[] CacheTags => [
		Definitions.CacheTags.ObjectContents,
		Definitions.CacheTags.ObjectOwnership,
		Definitions.CacheTags.ObjectList,
		Definitions.CacheTags.PlayerList];
}