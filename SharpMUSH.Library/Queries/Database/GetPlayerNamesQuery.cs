using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>A player's name and the aliases it answers to, as <c>renderplayers()</c> matches them.</summary>
public sealed record PlayerNameEntry(DBRef DBRef, string Name, string[] Aliases);

/// <summary>
/// Every player's name and aliases. Cached until a player is created, renamed, destroyed or has its
/// <c>ALIAS</c> written, each of which expires <see cref="Definitions.CacheTags.PlayerNames"/>.
/// </summary>
public record GetPlayerNamesQuery : IStreamQuery<PlayerNameEntry>, ICacheable
{
	public string CacheKey => "global:PlayerNameIndex";

	public string[] CacheTags => [Definitions.CacheTags.PlayerNames, Definitions.CacheTags.PlayerList];
}
