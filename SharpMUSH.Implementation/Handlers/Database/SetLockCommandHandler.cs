using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetLockCommandHandler(IObjectStore database, IBooleanExpressionParser booleanParser, ILockService lockService)
	: ICommandHandler<SetLockCommand, Result<Success>>
{
	public async ValueTask<Result<Success>> Handle(SetLockCommand request, CancellationToken cancellationToken)
	{
		var executor = request.Executor;
		if (await database.GetObjectNodeAsync(request.Target.DBRef, cancellationToken) is not AnySharpObject target)
			return new Error<string>("No such object.");
		if (await lockService.ResolveWriteNameAsync(target, request.LockName, cancellationToken) is not string name)
			return new Error<string>("Unknown lock type.");
		target.Object().Locks.TryGetValue(name, out var old);
		var flags = request.Flags ?? old?.Flags ?? lockService.SystemLocks.GetValueOrDefault(name);
		var data = new SharpLockData(request.LockString, flags, request.PreserveCreator ? request.Creator : executor.Object().DBRef);
		if (!await lockService.CanWriteAsync(executor, target, old ?? data) || !await lockService.CanWriteAsync(executor, target, data))
			return new Error<string>("Permission denied.");
		if (await booleanParser.BindAsync(request.LockString, executor, cancellationToken) is not string bound)
			return new Error<string>("I don't understand that key.");
		data = data with { LockString = bound };
		await database.SetLockAsync(target.Object(), name, data, cancellationToken);
		if (old is not null) booleanParser.InvalidateCache(old.LockString);
		target.Object().WithLock(name, data);
		request.Target.WithLock(name, data);
		return new Success();
	}
}

public class UnsetLockCommandHandler(IObjectStore database, IBooleanExpressionParser booleanParser, ILockService lockService)
	: ICommandHandler<UnsetLockCommand, Result<Success>>
{
	public async ValueTask<Result<Success>> Handle(UnsetLockCommand request, CancellationToken cancellationToken)
	{
		var executor = request.Executor;
		if (await database.GetObjectNodeAsync(request.Target.DBRef, cancellationToken) is not AnySharpObject target)
			return new Error<string>("No such object.");
		if (await lockService.ResolveWriteNameAsync(target, request.LockName, cancellationToken) is not string name) return new Error<string>("Unknown lock type.");
		target.Object().Locks.TryGetValue(name, out var old);
		if (!await lockService.CanWriteAsync(executor, target, old ?? new SharpLockData())) return new Error<string>("Permission denied.");
		if (old is null) return new Success();
		await database.UnsetLockAsync(target.Object(), name, cancellationToken);
		booleanParser.InvalidateCache(old.LockString);
		target.Object().WithoutLock(name);
		request.Target.WithoutLock(name);
		return new Success();
	}
}

public class SetLockFlagsCommandHandler(IObjectStore database, ILockService lockService)
	: ICommandHandler<SetLockFlagsCommand, Result<Success>>
{
	public async ValueTask<Result<Success>> Handle(SetLockFlagsCommand request, CancellationToken cancellationToken)
	{
		if (await database.GetObjectNodeAsync(request.Target.DBRef, cancellationToken) is not AnySharpObject target)
			return new Error<string>("No such object.");
		var name = LockNames.Canonical(request.LockName);
		if (!target.Object().Locks.TryGetValue(name, out var data)) return new Error<string>("No such lock.");
		if (!await lockService.CanWriteAsync(request.Executor, target, data)) return new Error<string>("Permission denied.");
		if (request.Flags.HasFlag(Library.Services.LockService.LockFlags.Wizard) && !await request.Executor.IsSee_All())
			return new Error<string>("Permission denied.");
		data = data with { Flags = request.Clear ? data.Flags & ~request.Flags : data.Flags | request.Flags };
		await database.SetLockAsync(target.Object(), name, data, cancellationToken);
		target.Object().WithLock(name, data);
		request.Target.WithLock(name, data);
		return new Success();
	}
}

public class CopyLockCommandHandler(IObjectStore database, ILockService lockService, IPermissionService permissions,
	IBooleanExpressionParser booleanParser) : ICommandHandler<CopyLockCommand, Result<Success>>
{
	public async ValueTask<Result<Success>> Handle(CopyLockCommand request, CancellationToken cancellationToken)
	{
		if (await database.GetObjectNodeAsync(request.Source.DBRef, cancellationToken) is not AnySharpObject source ||
			await database.GetObjectNodeAsync(request.Target.DBRef, cancellationToken) is not AnySharpObject target)
			return new Error<string>("No such object.");
		if (!await permissions.Controls(request.Executor, source)) return new Error<string>("Permission denied.");
		var name = LockNames.Canonical(request.LockName);
		if (!source.Object().Locks.TryGetValue(name, out var data)) return new Error<string>("No such lock.");
		if (data.Flags.HasFlag(Library.Services.LockService.LockFlags.NoClone)) return new Success();
		data = data with { Creator = request.Executor.Object().DBRef };
		target.Object().Locks.TryGetValue(name, out var old);
		if (!await lockService.CanWriteAsync(request.Executor, target, old ?? data) || !await lockService.CanWriteAsync(request.Executor, target, data))
			return new Error<string>("Permission denied.");
		// Persisted invalid expressions remain invalid and fail closed on the clone.
		await database.SetLockAsync(target.Object(), name, data, cancellationToken);
		if (old is not null) booleanParser.InvalidateCache(old.LockString);
		target.Object().WithLock(name, data);
		request.Target.WithLock(name, data);
		return new Success();
	}
}
