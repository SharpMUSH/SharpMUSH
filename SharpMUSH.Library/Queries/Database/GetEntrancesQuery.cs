using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Gets all exits that lead to a specific destination.
/// </summary>
/// <param name="Destination">The destination DBRef to find entrances for</param>
public record GetEntrancesQuery(DBRef Destination) : IStreamQuery<SharpExit>;
