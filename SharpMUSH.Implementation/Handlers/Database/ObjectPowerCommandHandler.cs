using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetObjectPowerCommandHandler(ISharpDatabase database) : ICommandHandler<SetObjectPowerCommand, bool>
{
	public async ValueTask<bool> Handle(SetObjectPowerCommand request, CancellationToken cancellationToken) 
		=> await database.SetObjectPowerAsync(request.Target, request.Flag);
}

public class UnsetObjectPowerCommandHandler(ISharpDatabase database) : ICommandHandler<UnsetObjectPowerCommand, bool>
{
	public async ValueTask<bool> Handle(UnsetObjectPowerCommand request, CancellationToken cancellationToken) 
		=> await database.UnsetObjectPowerAsync(request.Target, request.Flag);
}