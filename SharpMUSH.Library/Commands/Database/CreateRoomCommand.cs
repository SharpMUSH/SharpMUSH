using MediatR;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreateRoomCommand(string Name, SharpPlayer Creator) : IRequest<DBRef>;