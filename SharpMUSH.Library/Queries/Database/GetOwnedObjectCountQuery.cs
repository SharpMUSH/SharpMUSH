using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Query to get the count of objects owned by a player.
/// </summary>
public record GetOwnedObjectCountQuery(SharpPlayer Player) : IQuery<int>;
