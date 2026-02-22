using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetLockCommandHandler(ISharpDatabase database, IBooleanExpressionParser booleanParser, ILockService lockService) : ICommandHandler<SetLockCommand>
{
	public async ValueTask<Unit> Handle(SetLockCommand request, CancellationToken cancellationToken)
	{
		// Normalize the lock string by converting bare dbrefs to objids
		// This ensures locks won't match recycled dbrefs after objects are destroyed
		var normalizedLockString = booleanParser.Normalize(request.LockString);

		// Determine flags for this lock
		var flags = lockService.SystemLocks.GetValueOrDefault(request.LockName, Library.Services.LockService.LockFlags.Default);

		// Create lock data with the normalized string and flags
		var lockData = new Library.Models.SharpLockData(normalizedLockString, flags);

		await database.SetLockAsync(request.Target, request.LockName, lockData, cancellationToken);
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