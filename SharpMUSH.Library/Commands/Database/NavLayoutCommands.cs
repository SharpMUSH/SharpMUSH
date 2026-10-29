using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models.Portal.Navigation;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>Saves the portal navigation layout over the version it was read at.</summary>
public record SaveNavLayoutCommand(NavLayout Layout, long ExpectedVersion) : ICommand<NavLayoutSaveResult>, ICacheInvalidating
{
	public string[] CacheKeys => [GetNavLayoutQuery.Key];
	public string[] CacheTags => [];
}

/// <summary>Removes the portal navigation layout, back to the default.</summary>
public record ResetNavLayoutCommand : ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [GetNavLayoutQuery.Key];
	public string[] CacheTags => [];
}
