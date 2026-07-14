using Mediator;

namespace SharpMUSH.Library.Commands.Database;

public record SetServerSetupCompletedCommand(bool Value) : ICommand<Unit>;
