using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SetAttributeCommand(DBRef DBRef, string[] Attribute, string Value, SharpPlayer Owner) : IRequest<bool>;