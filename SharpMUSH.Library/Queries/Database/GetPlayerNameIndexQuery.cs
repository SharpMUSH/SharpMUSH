using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Every player's names, built into the index <c>renderplayers()</c> matches against. Cached per choice of
/// aliases and first names until a player is created, renamed, destroyed or has its <c>ALIAS</c> written,
/// each of which expires <see cref="Definitions.CacheTags.PlayerNames"/>.
/// </summary>
public record GetPlayerNameIndexQuery(bool Aliases, bool FirstNames) : IQuery<PlayerNameIndex>, ICacheable
{
	public string CacheKey => $"global:PlayerNameIndex:{(Aliases ? "aliases" : "names")}:{(FirstNames ? "first" : "full")}";

	public string[] CacheTags => [Definitions.CacheTags.PlayerNames, Definitions.CacheTags.PlayerList];
}
