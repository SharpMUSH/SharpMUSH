using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetObjectPowerCommandHandler(ISharpDatabase database) : ICommandHandler<SetObjectPowerCommand, bool>
{
	public async ValueTask<bool> Handle(SetObjectPowerCommand request, CancellationToken cancellationToken)
	{
		var set = await database.SetObjectPowerAsync(request.Target, request.Flag, cancellationToken);
		if (set)
		{
			await request.Target.Object().WithPower(request.Flag, cancellationToken);
		}

		return set;
	}
}

public class UnsetObjectPowerCommandHandler(ISharpDatabase database) : ICommandHandler<UnsetObjectPowerCommand, bool>
{
	public async ValueTask<bool> Handle(UnsetObjectPowerCommand request, CancellationToken cancellationToken)
	{
		var unset = await database.UnsetObjectPowerAsync(request.Target, request.Flag, cancellationToken);
		if (unset)
		{
			await request.Target.Object().WithoutPower(request.Flag.Name, cancellationToken);
		}

		return unset;
	}
}
