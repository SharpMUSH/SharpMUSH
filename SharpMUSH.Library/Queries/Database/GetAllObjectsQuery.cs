using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Gets all objects in the database as a streaming AsyncEnumerable.
/// This allows for efficient filtering and searching without loading all objects into memory.
/// </summary>
public record GetAllObjectsQuery() : IStreamQuery<SharpObject>;
