using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Gets all objects in the database as fully-typed <see cref="AnySharpObject"/> instances,
/// without routing through the per-object query cache.
/// Use this instead of <see cref="GetAllObjectsQuery"/> when the caller needs the typed object
/// (to avoid a secondary <c>GetObjectNodeQuery</c> inside the loop, which causes FusionCache
/// per-key lock contention with regular player commands).
/// </summary>
public record GetAllTypedObjectsQuery() : IStreamQuery<AnySharpObject>;
