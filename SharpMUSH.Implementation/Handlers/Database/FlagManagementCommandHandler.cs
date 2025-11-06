using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Handlers.Database;

public class CreateObjectFlagCommandHandler(ISharpDatabase database) 
	: ICommandHandler<CreateObjectFlagCommand, SharpObjectFlag?>
{
	public async ValueTask<SharpObjectFlag?> Handle(CreateObjectFlagCommand request, CancellationToken cancellationToken)
	{
		return await database.CreateObjectFlagAsync(
			request.Name,
			request.Aliases,
			request.Symbol,
			request.System,
			request.SetPermissions,
			request.UnsetPermissions,
			request.TypeRestrictions,
			cancellationToken);
	}
}

public class DeleteObjectFlagCommandHandler(ISharpDatabase database) 
	: ICommandHandler<DeleteObjectFlagCommand, bool>
{
	public async ValueTask<bool> Handle(DeleteObjectFlagCommand request, CancellationToken cancellationToken)
	{
		return await database.DeleteObjectFlagAsync(request.FlagName, cancellationToken);
	}
}
