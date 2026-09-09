using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class CreateExitCommandHandler(
	IObjectStore database,
	IFlagAndPowerStore flags,
	IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions> configuration)
	: ICommandHandler<CreateExitCommand, DBRef>
{
	public async ValueTask<DBRef> Handle(CreateExitCommand request, CancellationToken cancellationToken)
	{
		var created = await database.CreateExitAsync(request.Name, request.Aliases, request.Location, request.Creator, cancellationToken: cancellationToken);

		await DefaultObjectFlags.ApplyAsync(flags, database, created,
			configuration.CurrentValue.Flag.ExitFlags, cancellationToken: cancellationToken);

		return created;
	}
}
