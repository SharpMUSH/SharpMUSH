using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetLockCommandHandler(ISharpDatabase database) : ICommandHandler<SetLockCommand>
{
	public async ValueTask<Unit> Handle(SetLockCommand request, CancellationToken cancellationToken)
	{
		await database.SetLockAsync(request.Target, request.LockName, request.LockString);
		return new Unit();
	}
}
public class UnsetLockCommandHandler(ISharpDatabase database) : ICommandHandler<UnsetLockCommand>
{
	public async ValueTask<Unit> Handle(UnsetLockCommand request, CancellationToken cancellationToken)
	{
		await database.UnsetLockAsync(request.Target, request.LockName);
		return new Unit();
	}
}