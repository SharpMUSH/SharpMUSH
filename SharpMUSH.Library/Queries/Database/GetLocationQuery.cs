using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetLocationQuery(DBRef DBRef, int Depth = 1) : IQuery<AnyOptionalSharpContainer>/*, ICacheable*/;

public record GetCertainLocationQuery(string Key, int Depth = 1) : IQuery<AnySharpContainer>/*, ICacheable*/;