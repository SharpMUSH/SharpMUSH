using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreateAttributeEntryCommand(string Name, string[] DefaultFlags, string? Limit = null, string[]? EnumValues = null) : ICommand<SharpAttributeEntry?>;
