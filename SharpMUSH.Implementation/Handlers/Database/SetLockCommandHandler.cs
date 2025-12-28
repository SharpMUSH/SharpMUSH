using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetLockCommandHandler(ISharpDatabase database, IBooleanExpressionParser booleanParser) : ICommandHandler<SetLockCommand>
{
	public async ValueTask<Unit> Handle(SetLockCommand request, CancellationToken cancellationToken)
	{
		// Normalize the lock string by converting bare dbrefs to objids
		// This ensures locks won't match recycled dbrefs after objects are destroyed
		var normalizedLockString = booleanParser.Normalize(request.LockString);
		
		await database.SetLockAsync(request.Target, request.LockName, normalizedLockString, cancellationToken);
		return new Unit();
	}
}
public class UnsetLockCommandHandler(ISharpDatabase database) : ICommandHandler<UnsetLockCommand>
{
	public async ValueTask<Unit> Handle(UnsetLockCommand request, CancellationToken cancellationToken)
	{
		await database.UnsetLockAsync(request.Target, request.LockName, cancellationToken);
		return new Unit();
	}
}