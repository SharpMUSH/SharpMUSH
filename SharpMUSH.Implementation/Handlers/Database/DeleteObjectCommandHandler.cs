using Mediator;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class DeleteObjectCommandHandler(IObjectStore database, IUserDefinedFunctionService? functions = null)
	: ICommandHandler<DeleteObjectCommand, bool>
{
	public async ValueTask<bool> Handle(DeleteObjectCommand request, CancellationToken cancellationToken)
	{
		var deleted = await database.DeleteObjectAsync(request.Target, cancellationToken);
		if (deleted) functions?.InvalidateLocalDefinitions(request.Target);
		return deleted;
	}
}
