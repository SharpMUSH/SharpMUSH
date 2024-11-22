using MediatR;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetLockCommandHandler(ISharpDatabase database) : IRequestHandler<SetLockCommand>
{
	public async Task Handle(SetLockCommand request, CancellationToken cancellationToken)
	{
		await database.SetLockAsync(request.Target, request.LockName, request.LockString);
	}
}