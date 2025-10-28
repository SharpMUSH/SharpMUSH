using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetObjectFlagCommandHandler(ISharpDatabase database) : ICommandHandler<SetObjectFlagCommand, bool>
{
	public async ValueTask<bool> Handle(SetObjectFlagCommand request, CancellationToken cancellationToken)
	{
		return await database.SetObjectFlagAsync(request.Target, request.Flag, cancellationToken);
	}
}

public class UnsetObjectFlagCommandHandler(ISharpDatabase database) : ICommandHandler<UnsetObjectFlagCommand, bool>
{
	public async ValueTask<bool> Handle(UnsetObjectFlagCommand request, CancellationToken cancellationToken)
	{
		return await database.UnsetObjectFlagAsync(request.Target, request.Flag, cancellationToken);
	}
}