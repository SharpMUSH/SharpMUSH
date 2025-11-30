using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Gets a player by matching to their name or aliases.
/// </summary>
/// <param name="Name">Name the search, case-insensitive.</param>
public record GetPlayerQuery(string Name) : IStreamQuery<SharpPlayer>, ICacheable
{
	public string CacheKey => $"player_by_name:{Name.ToLowerInvariant()}";
	public string[] CacheTags => [Definitions.CacheTags.PlayerNames];
}