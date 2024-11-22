using MediatR;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record ClearAttributeCommand(DBRef DBRef, string[] Attribute) : IRequest<bool>;