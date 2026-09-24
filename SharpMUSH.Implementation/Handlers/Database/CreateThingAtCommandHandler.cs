using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class CreateThingAtCommandHandler(
	IObjectStore database,
	IFlagAndPowerStore flags,
	IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions> configuration)
	: ICommandHandler<CreateThingAtCommand, Result<DBRef>>
{
	public async ValueTask<Result<DBRef>> Handle(CreateThingAtCommand request, CancellationToken cancellationToken)
		=> await database.CreateThingAtAsync(request.Requested, request.Name, request.Where, request.Owner,
			request.Home, cancellationToken) switch
		{
			DBRef created => await StampedAsync(created, cancellationToken),
			Error<string> error => error
		};

	/// <summary>
	/// The configured <c>thing_flags</c>, unconditionally — unlike <see cref="CreateThingCommand"/>,
	/// which lets an import or a package install opt out. This command exists for a player typing
	/// <c>@create</c>, and neither of those callers names a dbref.
	/// </summary>
	private async ValueTask<Result<DBRef>> StampedAsync(DBRef created, CancellationToken cancellationToken)
	{
		await DefaultObjectFlags.ApplyAsync(flags, database, created,
			configuration.CurrentValue.Flag.ThingFlags, cancellationToken: cancellationToken);

		return created;
	}
}
