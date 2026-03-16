using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class ReassignAttributeOwnerCommandHandler(ISharpDatabase database)
	: ICommandHandler<ReassignAttributeOwnerCommand>
{
	public async ValueTask<Unit> Handle(ReassignAttributeOwnerCommand request, CancellationToken cancellationToken)
	{
		await database.ReassignAttributeOwnerAsync(request.OldOwner, request.NewOwner, cancellationToken);
		return Unit.Value;
	}
}
