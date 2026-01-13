using Mediator;

namespace SharpMUSH.Library.Commands.Database;

public record DeleteAttributeEntryCommand(string Name) : ICommand<bool>;
