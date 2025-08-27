using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SetExpandedDataCommand(SharpObject SharpObject, string TypeName, string Json) : ICommand;

public record SetExpandedServerDataCommand(string TypeName, string Json) : ICommand;