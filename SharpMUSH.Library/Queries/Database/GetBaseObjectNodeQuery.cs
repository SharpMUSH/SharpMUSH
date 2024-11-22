using MediatR;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

[CacheableQuery]
public record GetBaseObjectNodeQuery(DBRef DBRef) : IRequest<SharpObject?>;