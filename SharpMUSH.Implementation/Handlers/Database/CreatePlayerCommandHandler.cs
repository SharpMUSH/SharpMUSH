using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class CreatePlayerCommandHandler(
	IObjectStore database,
	IFlagAndPowerStore flags,
	IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions> configuration)
	: ICommandHandler<CreatePlayerCommand, DBRef>
{
	public async ValueTask<DBRef> Handle(CreatePlayerCommand request, CancellationToken cancellationToken)
	{
		var created = await database.CreatePlayerAsync(request.Name, request.Password, request.Location, request.Home,
			request.Quota, request.Salt, request.CreationTime, request.ModifiedTime, cancellationToken);

		if (request.ApplyDefaultFlags)
		{
			await DefaultObjectFlags.ApplyAsync(flags, database, created,
				configuration.CurrentValue.Flag.PlayerFlags, cancellationToken: cancellationToken);
		}

		return created;
	}
}
