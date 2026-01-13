using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record RenameAttributeEntryCommand(string OldName, string NewName) : ICommand<SharpAttributeEntry?>;
