using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Handlers.Database;

public class CreateRoomCommandHandler(ISharpDatabase database) : ICommandHandler<CreateRoomCommand, DBRef>
{
		public async ValueTask<DBRef> Handle(CreateRoomCommand request, CancellationToken cancellationToken)
		{
				return await database.CreateRoomAsync(request.Name, request.Creator);
		}
}