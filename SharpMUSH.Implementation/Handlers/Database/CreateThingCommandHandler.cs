using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Handlers.Database;

public class CreateThingCommandHandler(ISharpDatabase database) : ICommandHandler<CreateThingCommand, DBRef>
{
	public async ValueTask<DBRef> Handle(CreateThingCommand request, CancellationToken cancellationToken)
	{
		return await database.CreateThingAsync(request.Name, request.Where, request.Owner, request.Home, cancellationToken);
	}
}