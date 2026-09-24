using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class CreateExitAtCommandHandler(
	IObjectStore database,
	IFlagAndPowerStore flags,
	IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions> configuration)
	: ICommandHandler<CreateExitAtCommand, Result<DBRef>>
{
	public async ValueTask<Result<DBRef>> Handle(CreateExitAtCommand request, CancellationToken cancellationToken)
		=> await database.CreateExitAtAsync(request.Requested, request.Name, request.Aliases, request.Location,
			request.Creator, request.ModifiedTime, cancellationToken) switch
		{
			DBRef created => await StampedAsync(created, cancellationToken),
			Error<string> error => error
		};

	/// <summary>
	/// The configured <c>exit_flags</c>, unconditionally — unlike <see cref="CreateExitCommand"/>,
	/// which lets an import or a package install opt out. This command exists for a player typing
	/// <c>@open</c> or <c>@dig</c>, and neither of those callers names a dbref.
	/// </summary>
	private async ValueTask<Result<DBRef>> StampedAsync(DBRef created, CancellationToken cancellationToken)
	{
		await DefaultObjectFlags.ApplyAsync(flags, database, created,
			configuration.CurrentValue.Flag.ExitFlags, cancellationToken: cancellationToken);

		return created;
	}
}
