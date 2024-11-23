using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SetAttributeCommand(DBRef DBRef, string[] Attribute, string Value, SharpPlayer Owner) : ICommand<bool>, ICacheInvalidating;