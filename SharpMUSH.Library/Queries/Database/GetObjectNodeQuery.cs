using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Resolves an object by dbref, honoring the objid (creation-time) check. NOT cached itself — it delegates
/// the cached load to <see cref="GetObjectNodeByNumberQuery"/> (number-keyed) and applies the timestamp
/// check on top, so the objid/recycle validation runs on every request rather than being cached.
/// </summary>
public record GetObjectNodeQuery(DBRef DBRef) : IQuery<AnyOptionalSharpObject>;
