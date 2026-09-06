using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class DeleteObjectCommandHandler(IObjectStore database)
	: ICommandHandler<DeleteObjectCommand, bool>
{
	public async ValueTask<bool> Handle(DeleteObjectCommand request, CancellationToken cancellationToken)
	{
		return await database.DeleteObjectAsync(request.Target, cancellationToken);
	}
}
