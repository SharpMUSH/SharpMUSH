using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Gets objects from the database with optional filtering at the database level.
/// This allows for efficient searching without loading all objects into memory.
/// Lock evaluation must happen in application code, but other filters can be pushed to the database.
/// </summary>
/// <param name="Filter">Filter criteria to apply at database level</param>
public record GetFilteredObjectsQuery(ObjectSearchFilter? Filter = null) : IStreamQuery<SharpObject>;
