using Mediator;
using OneOf;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetExitsQuery(OneOf<DBRef, AnySharpContainer> DBRef) : IQuery<IEnumerable<SharpExit>>, ICacheable;