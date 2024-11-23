using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

[CacheableQuery]
public record GetLocationQuery(DBRef DBRef, int Depth = 1) : IRequest<AnyOptionalSharpContainer>;

[CacheableQuery]
public record GetCertainLocationQuery(string Key, int Depth = 1) : IRequest<AnySharpContainer>;