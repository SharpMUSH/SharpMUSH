using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetObjectNameCommandHandler(ISharpDatabase database) : ICommandHandler<SetNameCommand, Unit>
{
	public async ValueTask<Unit> Handle(SetNameCommand request, CancellationToken cancellationToken)
	{
		await database.SetObjectName(request.Target, request.Name, cancellationToken);
		return Unit.Value;
	}
}

public class SetObjectHomeCommandHandler(ISharpDatabase database) : ICommandHandler<SetObjectHomeCommand, Unit>
{
	public async ValueTask<Unit> Handle(SetObjectHomeCommand request, CancellationToken cancellationToken)
	{
		await database.SetContentHome(request.Target, request.Home, cancellationToken);
		return Unit.Value;
	}
}

public class SetObjectLocationCommandHandler(ISharpDatabase database) : ICommandHandler<SetObjectLocationCommand, Unit>
{
	public async ValueTask<Unit> Handle(SetObjectLocationCommand request, CancellationToken cancellationToken)
	{
		await database.SetContentLocation(request.Target, request.Container);
		return Unit.Value;
	}
}

public class SetObjectOwnerCommandHandler(ISharpDatabase database) : ICommandHandler<SetObjectOwnerCommand, Unit>
{
	public async ValueTask<Unit> Handle(SetObjectOwnerCommand request, CancellationToken cancellationToken)
	{
		await database.SetObjectOwner(request.Target, request.Owner, cancellationToken);
		return Unit.Value;
	}
}

public class SetObjectParentCommandHandler(ISharpDatabase database) : ICommandHandler<SetObjectParentCommand, Unit>
{
	public async ValueTask<Unit> Handle(SetObjectParentCommand request, CancellationToken cancellationToken)
	{
		await database.SetObjectParent(request.Target, request.Parent, cancellationToken);
		return Unit.Value;
	}
}

public class UnsetObjectParentCommandHandler(ISharpDatabase database) : ICommandHandler<UnsetObjectParentCommand, Unit>
{
	public async ValueTask<Unit> Handle(UnsetObjectParentCommand request, CancellationToken cancellationToken)
	{
		await database.UnsetObjectParent(request.Target, cancellationToken);
		return Unit.Value;
	}
}

public class SetObjectZoneCommandHandler(ISharpDatabase database) : ICommandHandler<SetObjectZoneCommand, Unit>
{
	public async ValueTask<Unit> Handle(SetObjectZoneCommand request, CancellationToken cancellationToken)
	{
		await database.SetObjectZone(request.Target, request.Zone, cancellationToken);
		return Unit.Value;
	}
}

public class UnsetObjectZoneCommandHandler(ISharpDatabase database) : ICommandHandler<UnsetObjectZoneCommand, Unit>
{
	public async ValueTask<Unit> Handle(UnsetObjectZoneCommand request, CancellationToken cancellationToken)
	{
		await database.UnsetObjectZone(request.Target, cancellationToken);
		return Unit.Value;
	}
}