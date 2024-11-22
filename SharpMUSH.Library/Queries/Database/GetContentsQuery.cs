using MediatR;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

[CacheableQuery]
public record GetContentsQuery(DBRef DBRef) : IRequest<IEnumerable<AnySharpContent>?>;