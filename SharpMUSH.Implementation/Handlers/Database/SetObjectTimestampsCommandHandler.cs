using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetObjectTimestampsCommandHandler(IObjectStore database) : ICommandHandler<SetObjectTimestampsCommand>
{
	public async ValueTask<Unit> Handle(SetObjectTimestampsCommand request, CancellationToken cancellationToken)
	{
		await database.SetObjectTimestampsAsync(request.Target, request.CreationTime, request.ModifiedTime,
			cancellationToken);
		return Unit.Value;
	}
}
