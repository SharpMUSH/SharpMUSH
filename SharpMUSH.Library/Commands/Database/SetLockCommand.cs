using MediatR;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SetLockCommand(SharpObject Target, string LockName, string LockString) : IRequest;