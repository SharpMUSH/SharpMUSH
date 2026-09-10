using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class CreateThingCommandHandler(
	IObjectStore database,
	IFlagAndPowerStore flags,
	IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions> configuration)
	: ICommandHandler<CreateThingCommand, DBRef>
{
	public async ValueTask<DBRef> Handle(CreateThingCommand request, CancellationToken cancellationToken)
	{
		var created = await database.CreateThingAsync(request.Name, request.Where, request.Owner, request.Home, cancellationToken: cancellationToken);

		if (request.ApplyDefaultFlags)
		{
			await DefaultObjectFlags.ApplyAsync(flags, database, created,
				configuration.CurrentValue.Flag.ThingFlags, cancellationToken: cancellationToken);
		}

		return created;
	}
}
