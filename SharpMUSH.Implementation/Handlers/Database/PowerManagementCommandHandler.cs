using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Handlers.Database;

public class CreatePowerCommandHandler(ISharpDatabase database)
	: ICommandHandler<CreatePowerCommand, SharpPower?>
{
	public async ValueTask<SharpPower?> Handle(CreatePowerCommand request, CancellationToken cancellationToken)
	{
		return await database.CreatePowerAsync(
			request.Name,
			request.Alias,
			request.System,
			request.SetPermissions,
			request.UnsetPermissions,
			request.TypeRestrictions,
			cancellationToken);
	}
}

public class DeletePowerCommandHandler(ISharpDatabase database)
	: ICommandHandler<DeletePowerCommand, bool>
{
	public async ValueTask<bool> Handle(DeletePowerCommand request, CancellationToken cancellationToken)
	{
		return await database.DeletePowerAsync(request.PowerName, cancellationToken);
	}
}
