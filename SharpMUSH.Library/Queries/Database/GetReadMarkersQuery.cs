using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Every read marker <paramref name="Character"/> (an objid) has. Not cached: a marker moves every time
/// its character reads a line, and nothing but the portal's load reads them back.
/// </summary>
public record GetReadMarkersQuery(DBRef Character) : IQuery<IReadOnlyList<SharpReadMarker>>;
