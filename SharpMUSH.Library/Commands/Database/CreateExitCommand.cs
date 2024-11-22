using MediatR;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreateExitCommand(string Name, AnySharpContainer Location, SharpPlayer Creator) : IRequest<DBRef>;