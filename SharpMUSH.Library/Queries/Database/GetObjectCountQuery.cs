using Mediator;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// The total number of objects in the database — PennMUSH's <c>db_top</c>, which the INFO socket
/// command reports as "Size". Counted in the store rather than by streaming
/// <see cref="GetAllObjectsQuery"/> and counting: INFO answers before login and is polled by MUD
/// listing crawlers, so it must not walk the database once per request.
/// </summary>
public record GetObjectCountQuery : IQuery<int>;
