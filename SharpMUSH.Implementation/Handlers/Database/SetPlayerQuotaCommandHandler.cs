using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetPlayerQuotaCommandHandler(ISharpDatabase database) : ICommandHandler<SetPlayerQuotaCommand>
{
	public async ValueTask<Unit> Handle(SetPlayerQuotaCommand request, CancellationToken cancellationToken)
	{
		await database.SetPlayerQuotaAsync(request.Player, request.Quota, cancellationToken);
		return Unit.Value;
	}
}
