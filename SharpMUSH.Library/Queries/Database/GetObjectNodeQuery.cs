using MediatR;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetObjectNodeQuery(DBRef DBRef) : IRequest<AnyOptionalSharpObject>;