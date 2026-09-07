using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetObjectFlagCommandHandler(IFlagAndPowerStore database) : ICommandHandler<SetObjectFlagCommand, bool>
{
	public async ValueTask<bool> Handle(SetObjectFlagCommand request, CancellationToken cancellationToken)
	{
		var set = await database.SetObjectFlagAsync(request.Target, request.Flag, cancellationToken);
		if (set)
		{
			await request.Target.Object().WithFlag(request.Flag, cancellationToken);
		}

		return set;
	}
}

public class UnsetObjectFlagCommandHandler(IFlagAndPowerStore database) : ICommandHandler<UnsetObjectFlagCommand, bool>
{
	public async ValueTask<bool> Handle(UnsetObjectFlagCommand request, CancellationToken cancellationToken)
	{
		var unset = await database.UnsetObjectFlagAsync(request.Target, request.Flag, cancellationToken);
		if (unset)
		{
			await request.Target.Object().WithoutFlag(request.Flag.Name, cancellationToken);
		}

		return unset;
	}
}
