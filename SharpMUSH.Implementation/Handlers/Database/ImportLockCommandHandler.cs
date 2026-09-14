using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library;

namespace SharpMUSH.Implementation.Handlers.Database;

public class ImportLockCommandHandler(IObjectStore database) : ICommandHandler<ImportLockCommand>
{
	public async ValueTask<Unit> Handle(ImportLockCommand request, CancellationToken cancellationToken)
	{
		await database.SetLockAsync(request.Target, LockNames.Canonical(request.LockName), request.Lock, cancellationToken);
		return Unit.Value;
	}
}
