using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class CreateRoomCommandHandler(
	IObjectStore database,
	IFlagAndPowerStore flags,
	IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions> configuration)
	: ICommandHandler<CreateRoomCommand, DBRef>
{
	public async ValueTask<DBRef> Handle(CreateRoomCommand request, CancellationToken cancellationToken)
	{
		var created = await database.CreateRoomAsync(request.Name, request.Creator, cancellationToken: cancellationToken);

		if (request.ApplyDefaultFlags)
		{
			await DefaultObjectFlags.ApplyAsync(flags, database, created,
				configuration.CurrentValue.Flag.RoomFlags, cancellationToken: cancellationToken);
		}

		return created;
	}
}
