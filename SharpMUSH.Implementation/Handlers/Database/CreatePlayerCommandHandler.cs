using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Handlers.Database;

public class CreatePlayerCommandHandler(ISharpDatabase database) : IRequestHandler<CreatePlayerCommand, DBRef>
{
	public async ValueTask<DBRef> Handle(CreatePlayerCommand request, CancellationToken cancellationToken)
	{
		return await database.CreatePlayerAsync(request.Name, request.Password, request.Location);
	}
}