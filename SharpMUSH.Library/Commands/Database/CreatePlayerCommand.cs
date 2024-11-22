using MediatR;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreatePlayerCommand(string Name, string Password, DBRef Location) : IRequest<DBRef>;