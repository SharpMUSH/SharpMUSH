using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class CreateRoomAtCommandHandler(
	IObjectStore database,
	IFlagAndPowerStore flags,
	IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions> configuration)
	: ICommandHandler<CreateRoomAtCommand, Result<DBRef>>
{
	public async ValueTask<Result<DBRef>> Handle(CreateRoomAtCommand request, CancellationToken cancellationToken)
		=> await database.CreateRoomAtAsync(request.Requested, request.Name, request.Creator, request.ModifiedTime,
			cancellationToken) switch
		{
			DBRef created => await StampedAsync(created, cancellationToken),
			Error<string> error => error
		};

	/// <summary>
	/// The configured <c>room_flags</c>, unconditionally — unlike <see cref="CreateRoomCommand"/>,
	/// which lets an import or a package install opt out. This command exists for a player typing
	/// <c>@dig</c>, and neither of those callers names a dbref.
	/// </summary>
	private async ValueTask<Result<DBRef>> StampedAsync(DBRef created, CancellationToken cancellationToken)
	{
		await DefaultObjectFlags.ApplyAsync(flags, database, created,
			configuration.CurrentValue.Flag.RoomFlags, cancellationToken: cancellationToken);

		return created;
	}
}
