using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Handlers.Database;

public class CreateExitCommandHandler(ISharpDatabase database) : ICommandHandler<CreateExitCommand, DBRef>
{
	public async ValueTask<DBRef> Handle(CreateExitCommand request, CancellationToken cancellationToken)
		=> await database.CreateExitAsync(request.Name, request.Aliases, request.Location, request.Creator, cancellationToken);
}