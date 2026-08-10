using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <inheritdoc cref="CreateThingCommandHandler"/>
public class CreateRoomCommandHandler(ISharpDatabase database, IFusionCache cache)
	: ICommandHandler<CreateRoomCommand, DBRef>
{
	public async ValueTask<DBRef> Handle(CreateRoomCommand request, CancellationToken cancellationToken)
	{
		var created = await database.CreateRoomAsync(request.Name, request.Creator, cancellationToken);
		await cache.RemoveAsync(CacheKeys.Object(created), token: cancellationToken);
		return created;
	}
}
