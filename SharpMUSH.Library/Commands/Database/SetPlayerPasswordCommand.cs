using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SetPlayerPasswordCommand(SharpPlayer Player, string Password) : ICommand<ValueTask<Unit>> { }