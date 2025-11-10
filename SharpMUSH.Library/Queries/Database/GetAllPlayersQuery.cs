using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Gets all players in the database as a streaming AsyncEnumerable.
/// This allows for efficient processing of all players without loading them all into memory.
/// </summary>
public record GetAllPlayersQuery() : IStreamQuery<SharpPlayer>;
