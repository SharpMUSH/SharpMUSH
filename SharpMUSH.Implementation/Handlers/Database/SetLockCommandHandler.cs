using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetLockCommandHandler(IObjectStore database, IBooleanExpressionParser booleanParser, ILockService lockService) : ICommandHandler<SetLockCommand>
{
	public async ValueTask<Unit> Handle(SetLockCommand request, CancellationToken cancellationToken)
	{
		// A standard lock is always stored under its LockType spelling, because that is the only one
		// LockService.Get looks up and a miss there reads as "unlocked", which passes everybody.
		var lockName = LockNames.Canonical(request.LockName);

		// Invalidate any previously compiled expression for the old lock text.
		// The old lock text comes from the in-memory object (no extra DB round-trip).
		if (request.Target.Locks.TryGetValue(lockName, out var oldLock)
			&& oldLock.LockString is not "#TRUE" and not null)
		{
			booleanParser.InvalidateCache(oldLock.LockString);
		}

		// Normalize the lock string by converting bare dbrefs to objids
		// This ensures locks won't match recycled dbrefs after objects are destroyed
		var normalizedLockString = booleanParser.Normalize(request.LockString, request.Executor);

		var flags = request.Flags ?? lockService.SystemLocks.GetValueOrDefault(lockName, Library.Services.LockService.LockFlags.Default);

		var lockData = new Library.Models.SharpLockData(normalizedLockString, flags);

		await database.SetLockAsync(request.Target, lockName, lockData, cancellationToken);

		// The loaded object is a snapshot; the command's own CacheKeys expire the cached one.
		request.Target.WithLock(lockName, lockData);

		return new Unit();
	}
}
public class UnsetLockCommandHandler(IObjectStore database, IBooleanExpressionParser booleanParser) : ICommandHandler<UnsetLockCommand>
{
	public async ValueTask<Unit> Handle(UnsetLockCommand request, CancellationToken cancellationToken)
	{
		var lockName = LockNames.Canonical(request.LockName);

		// Invalidate the compiled expression for the lock being removed
		if (request.Target.Locks.TryGetValue(lockName, out var oldLock)
			&& oldLock.LockString is not "#TRUE" and not null)
		{
			booleanParser.InvalidateCache(oldLock.LockString);
		}

		await database.UnsetLockAsync(request.Target, lockName, cancellationToken);

		request.Target.WithoutLock(lockName);

		return new Unit();
	}
}
